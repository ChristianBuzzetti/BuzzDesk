using System.Text;
using BuzzDesk.Domain;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>Tasks: lists, edits, completion (with the next instance of a series), postpone, subtasks and tags.</summary>
public static class Tasks
{
    const string Select = """
        SELECT t.id, t.section_id, t.parent_task_id, t.topic_id, t.recurrence_id, t.title, t.notes, t.priority,
               t.due_date, t.due_time, t.status, t.completed_utc, t.postpone_count, t.sort_order,
               t.created_utc, t.updated_utc, t.deleted_utc,
               s.name AS section_name, tp.title AS topic_title,
               r.freq, r.interval, r.weekdays_mask, r.month_day, r.until_date,
               (SELECT group_concat(g.name, char(31)) FROM task_tag x JOIN tag g ON g.id = x.tag_id WHERE x.task_id = t.id) AS tags,
               (SELECT count(*) FROM task c WHERE c.parent_task_id = t.id AND c.deleted_utc IS NULL) AS sub_total,
               (SELECT count(*) FROM task c WHERE c.parent_task_id = t.id AND c.deleted_utc IS NULL AND c.status = 'done') AS sub_done
          FROM task t
          LEFT JOIN section s     ON s.id = t.section_id
          LEFT JOIN topic tp      ON tp.id = t.topic_id
          LEFT JOIN recurrence r  ON r.id = t.recurrence_id
        """;

    static TaskItem Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), SectionId = Db.Long(r, "section_id"), ParentId = Db.Long(r, "parent_task_id"), TopicId = Db.Long(r, "topic_id"),
        RecurrenceId = Db.Long(r, "recurrence_id"), Title = r.GetString(5), Notes = Db.Str(r, "notes"), Priority = (int)r.GetInt64(7),
        DueDate = Db.DateOrNull(r, "due_date"), DueTime = Db.TimeOrNull(r, "due_time"), Status = TaskItem.FromDb(r.GetString(10)),
        CompletedUtc = Db.UtcOrNull(r, "completed_utc"), PostponeCount = (int)r.GetInt64(12), SortOrder = (int)r.GetInt64(13),
        CreatedUtc = Db.ParseUtc(r.GetString(14)), UpdatedUtc = Db.ParseUtc(r.GetString(15)), DeletedUtc = Db.UtcOrNull(r, "deleted_utc"),
        SectionName = Db.Str(r, "section_name"), TopicTitle = Db.Str(r, "topic_title"),
        Recurrence = Db.Str(r, "freq") is string f
            ? new RecurrenceRule(RecurrenceRule.FromDb(f), (int)r.GetInt64(20), r.IsDBNull(21) ? null : (int)r.GetInt64(21), r.IsDBNull(22) ? null : (int)r.GetInt64(22), Db.DateOrNull(r, "until_date"))
            : null,
        Tags = Db.Str(r, "tags") is string tags ? tags.Split('\u001f').OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() : [],
        SubtaskCount = (int)r.GetInt64(25), SubtaskDone = (int)r.GetInt64(26),
    };

    /// <summary>Top-level tasks for a list. <paramref name="today"/> is the user's day (Clock.Today).</summary>
    public static List<TaskItem> List(TaskFilter f, DateOnly today) => Repo.Read(c =>
    {
        var sql = new StringBuilder(Select).Append(" WHERE t.deleted_utc IS NULL AND t.parent_task_id IS NULL");
        var args = new List<(string, object?)> { ("$today", Db.Date(today)), ("$limit", f.Limit) };
        switch (f.Scope)
        {
            case TaskScope.Open: sql.Append(" AND t.status = 'open'"); break;
            case TaskScope.Today: sql.Append(" AND t.status = 'open' AND t.due_date <= $today"); break;
            case TaskScope.Upcoming: sql.Append(" AND t.status = 'open' AND t.due_date > $today"); break;
            case TaskScope.NoDate: sql.Append(" AND t.status = 'open' AND t.due_date IS NULL"); break;
            case TaskScope.Done: sql.Append(" AND t.status = 'done'"); break;
        }
        if (f.SectionId is long sid) { sql.Append(" AND (t.section_id = $sid OR t.section_id IN (SELECT id FROM section WHERE parent_id = $sid))"); args.Add(("$sid", sid)); }
        if (f.TagId is long tid) { sql.Append(" AND EXISTS (SELECT 1 FROM task_tag x WHERE x.task_id = t.id AND x.tag_id = $tid)"); args.Add(("$tid", tid)); }
        if (f.TopicId is long topic) { sql.Append(" AND t.topic_id = $topic"); args.Add(("$topic", topic)); }
        if (!string.IsNullOrWhiteSpace(f.Text)) { sql.Append(" AND (t.title LIKE $text ESCAPE '\\' OR t.notes LIKE $text ESCAPE '\\')"); args.Add(("$text", "%" + Like(f.Text.Trim()) + "%")); }
        sql.Append(f.Scope == TaskScope.Done
            ? " ORDER BY t.completed_utc DESC"
            : " ORDER BY t.due_date IS NULL, t.due_date, t.due_time IS NULL, t.due_time, t.priority DESC, t.sort_order, t.id");
        sql.Append(" LIMIT $limit");
        return Db.Query(c, sql.ToString(), Map, args.ToArray());
    });

    public static TaskItem? Get(long id) => Repo.Read(c => Get(c, id));
    static TaskItem? Get(SqliteConnection c, long id) => Db.Query(c, Select + " WHERE t.id = $id", Map, ("$id", id)).FirstOrDefault();

    public static List<TaskItem> Subtasks(long parentId) => Repo.Read(c =>
        Db.Query(c, Select + " WHERE t.parent_task_id = $p AND t.deleted_utc IS NULL ORDER BY t.sort_order, t.id", Map, ("$p", parentId)));

    /// <summary>Open top-level tasks: due today or earlier (overdue count apart), and all open.</summary>
    public static (int dueToday, int overdue, int open) Counts(DateOnly today) => Repo.Read(c =>
    {
        using var cmd = Db.Cmd(c, """
            SELECT coalesce(sum(due_date = $today), 0), coalesce(sum(due_date < $today), 0), count(*)
              FROM task WHERE status = 'open' AND deleted_utc IS NULL AND parent_task_id IS NULL
            """, ("$today", Db.Date(today)));
        using var r = cmd.ExecuteReader();
        r.Read();
        return ((int)r.GetInt64(0), (int)r.GetInt64(1), (int)r.GetInt64(2));
    });

    // ------------------------------------------------------------------ writes
    public sealed record Draft(string Title, long? SectionId = null, int Priority = 0, DateOnly? DueDate = null, TimeOnly? DueTime = null,
        string? Notes = null, IReadOnlyList<string>? Tags = null, RecurrenceRule? Recurrence = null, long? ParentId = null, long? TopicId = null);

    public static long Add(Draft d, DateOnly today) => Repo.Write("task", (c, _) => Insert(c, d, today));

    static long Insert(SqliteConnection c, Draft d, DateOnly today)
    {
        string now = Db.NowUtc();
        var due = d.DueDate;
        long? recurrenceId = null;
        if (d.Recurrence is RecurrenceRule rule && d.ParentId == null)
        {
            due ??= rule.FirstOnOrAfter(today);
            recurrenceId = InsertRule(c, rule.Normalized(due ?? today));
        }
        int order = (int)(Db.Scalar<long?>(c, "SELECT max(sort_order) FROM task WHERE coalesce(parent_task_id, 0) = coalesce($p, 0)", ("$p", d.ParentId)) ?? 0) + 1;
        long id = Db.Insert(c, """
            INSERT INTO task(section_id, parent_task_id, topic_id, recurrence_id, title, notes, priority, due_date, due_time, sort_order, created_utc, updated_utc)
            VALUES ($s, $p, $tp, $r, $title, $notes, $prio, $due, $time, $o, $now, $now)
            """, ("$s", d.SectionId), ("$p", d.ParentId), ("$tp", d.TopicId), ("$r", recurrenceId), ("$title", d.Title.Trim()), ("$notes", Blank(d.Notes)),
            ("$prio", Math.Clamp(d.Priority, 0, 3)), ("$due", Db.Value(due)), ("$time", due == null ? null : Db.Value(d.DueTime)), ("$o", order), ("$now", now));
        if (d.Tags is { Count: > 0 }) SetTags(c, id, d.Tags);
        return id;
    }

    static long InsertRule(SqliteConnection c, RecurrenceRule rule) => Db.Insert(c,
        "INSERT INTO recurrence(freq, interval, weekdays_mask, month_day, until_date, created_utc) VALUES ($f, $i, $w, $m, $u, $t)",
        ("$f", RecurrenceRule.ToDb(rule.Freq)), ("$i", Math.Clamp(rule.Interval, 1, 365)), ("$w", rule.WeekdaysMask), ("$m", rule.MonthDay), ("$u", Db.Value(rule.Until)), ("$t", Db.NowUtc()));

    /// <summary>Saves the editable fields. A changed rule replaces the series' rule for this and future instances.</summary>
    public static void Update(TaskItem t, IReadOnlyList<string>? tags = null) => Repo.Write("task", (c, _) =>
    {
        long? recurrenceId = t.RecurrenceId;
        var current = Get(c, t.Id) ?? throw new InvalidOperationException("Task not found: " + t.Id);
        if (t.Recurrence == null) recurrenceId = null;
        else if (current.Recurrence != t.Recurrence || recurrenceId == null)
            recurrenceId = InsertRule(c, t.Recurrence.Normalized(t.DueDate ?? current.DueDate ?? DateOnly.FromDateTime(DateTime.Today)));
        Db.Exec(c, """
            UPDATE task SET title = $title, notes = $notes, priority = $prio, section_id = $s, topic_id = $tp, due_date = $due, due_time = $time,
                            recurrence_id = $r, updated_utc = $now
             WHERE id = $id
            """, ("$title", t.Title.Trim()), ("$notes", Blank(t.Notes)), ("$prio", Math.Clamp(t.Priority, 0, 3)), ("$s", t.SectionId), ("$tp", t.TopicId),
            ("$due", Db.Value(t.DueDate)), ("$time", t.DueDate == null ? null : Db.Value(t.DueTime)), ("$r", recurrenceId), ("$now", Db.NowUtc()), ("$id", t.Id));
        if (tags != null) SetTags(c, t.Id, tags);
        // A series' old rule row is kept while instances (done ones too) still point at it; orphans go.
        Db.Exec(c, "DELETE FROM recurrence WHERE id NOT IN (SELECT recurrence_id FROM task WHERE recurrence_id IS NOT NULL)");
    });

    static void SetTags(SqliteConnection c, long taskId, IEnumerable<string> names)
    {
        var ids = Tags.Ensure(c, names);
        Db.Exec(c, "DELETE FROM task_tag WHERE task_id = $t", ("$t", taskId));
        foreach (long id in ids) Db.Exec(c, "INSERT INTO task_tag(task_id, tag_id) VALUES ($t, $g)", ("$t", taskId), ("$g", id));
        Tags.PruneUnused(c);
    }

    /// <summary>Marks the task done. For a recurring task, creates the next instance (same fields, tags, subtasks
    /// reset to open, reminders moved by the same offset) in the same transaction and returns its id.</summary>
    public static long? Complete(long id, DateOnly today) => Repo.Write("task", (c, _) =>
    {
        var t = Get(c, id);
        if (t == null || !t.IsOpen) return (long?)null;
        string now = Db.NowUtc();
        Db.Exec(c, "UPDATE task SET status = 'done', completed_utc = $now, updated_utc = $now WHERE id = $id", ("$now", now), ("$id", id));
        if (t.ParentId != null || t.Recurrence is not RecurrenceRule rule) return null;
        var from = t.DueDate ?? today;
        var next = rule.Next(from);
        // A late completion should not create instances already in the past: skip ahead to today or later.
        while (next is DateOnly n && n < today && rule.Next(n) is DateOnly later) next = later;
        if (next is not DateOnly nextDue) return null;
        long nextId = Db.Insert(c, """
            INSERT INTO task(section_id, topic_id, recurrence_id, title, notes, priority, due_date, due_time, sort_order, created_utc, updated_utc)
            SELECT section_id, topic_id, recurrence_id, title, notes, priority, $due, due_time, sort_order, $now, $now FROM task WHERE id = $id
            """, ("$due", Db.Date(nextDue)), ("$now", now), ("$id", id));
        Db.Exec(c, "INSERT INTO task_tag(task_id, tag_id) SELECT $n, tag_id FROM task_tag WHERE task_id = $id", ("$n", nextId), ("$id", id));
        Db.Exec(c, """
            INSERT INTO task(section_id, parent_task_id, title, notes, priority, sort_order, created_utc, updated_utc)
            SELECT section_id, $n, title, notes, priority, sort_order, $now, $now FROM task WHERE parent_task_id = $id AND deleted_utc IS NULL ORDER BY sort_order, id
            """, ("$n", nextId), ("$now", now), ("$id", id));
        // Reminders of the instance move with it (same offset from the due date), so a weekly reminder stays weekly.
        int shift = nextDue.DayNumber - from.DayNumber;
        Db.Exec(c, """
            INSERT INTO reminder(task_id, remind_at_utc, created_utc)
            SELECT $n, strftime('%Y-%m-%dT%H:%M:%SZ', remind_at_utc, $shift || ' days'), $now FROM reminder WHERE task_id = $id
            """, ("$n", nextId), ("$shift", shift), ("$now", now), ("$id", id));
        Db.Exec(c, "UPDATE reminder SET dismissed_utc = coalesce(dismissed_utc, $now) WHERE task_id = $id", ("$now", now), ("$id", id));
        return nextId;
    });

    /// <summary>Back to open (undo a completion). The next instance a recurring completion created is left alone.</summary>
    public static void Reopen(long id) => Repo.Write("task", (c, _) =>
        Db.Exec(c, "UPDATE task SET status = 'open', completed_utc = NULL, updated_utc = $now WHERE id = $id", ("$now", Db.NowUtc()), ("$id", id)));

    /// <summary>Moves the due date and counts the postponement. Pending reminders move by the same number of days.</summary>
    public static void Postpone(long id, DateOnly newDue) => Repo.Write("task", (c, _) =>
    {
        var old = Db.Scalar<string>(c, "SELECT due_date FROM task WHERE id = $id", ("$id", id));
        Db.Exec(c, "UPDATE task SET due_date = $d, postpone_count = postpone_count + 1, updated_utc = $now WHERE id = $id",
            ("$d", Db.Date(newDue)), ("$now", Db.NowUtc()), ("$id", id));
        if (old != null)
        {
            int shift = newDue.DayNumber - Db.ParseDate(old).DayNumber;
            Db.Exec(c, """
                UPDATE reminder SET remind_at_utc = strftime('%Y-%m-%dT%H:%M:%SZ', remind_at_utc, $shift || ' days'), snoozed_until_utc = NULL, fired_utc = NULL
                 WHERE task_id = $id AND dismissed_utc IS NULL
                """, ("$shift", shift), ("$id", id));
        }
    });

    /// <summary>To the trash (with its subtasks); <see cref="Restore"/> brings it back.</summary>
    public static void Delete(long id) => Repo.Write("task", (c, _) =>
        Db.Exec(c, "UPDATE task SET deleted_utc = $now WHERE (id = $id OR parent_task_id = $id) AND deleted_utc IS NULL", ("$now", Db.NowUtc()), ("$id", id)));

    public static void Restore(long id) => Repo.Write("task", (c, _) =>
        Db.Exec(c, "UPDATE task SET deleted_utc = NULL WHERE id = $id OR parent_task_id = $id", ("$id", id)));

    public static void SetPriority(long id, int priority) => Repo.Write("task", (c, _) =>
        Db.Exec(c, "UPDATE task SET priority = $p, updated_utc = $now WHERE id = $id", ("$p", Math.Clamp(priority, 0, 3)), ("$now", Db.NowUtc()), ("$id", id)));

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    public static string Like(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
