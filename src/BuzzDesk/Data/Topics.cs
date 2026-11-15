using System.Text;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

public enum TopicState { Active, Waiting, Paused, Closed }

public sealed record Topic
{
    public long Id { get; init; }
    public long? SectionId { get; init; }
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public TopicState Status { get; init; }
    public long? WaitingOnId { get; init; }
    public string? WaitingOn { get; init; }
    public DateTime? WaitingSinceUtc { get; init; }
    public DateOnly? NextReview { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public DateTime? ClosedUtc { get; init; }
    // joined
    public string? SectionName { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int OpenTasks { get; init; }
    public int Notes { get; init; }
    public DateTime? LastNoteUtc { get; init; }

    public bool ReviewDue(DateOnly today) => Status != TopicState.Closed && NextReview is DateOnly d && d <= today;

    public static string ToDb(TopicState s) => s switch { TopicState.Active => "active", TopicState.Waiting => "waiting", TopicState.Paused => "paused", _ => "closed" };
    public static TopicState FromDb(string s) => s switch { "active" => TopicState.Active, "waiting" => TopicState.Waiting, "paused" => TopicState.Paused, _ => TopicState.Closed };
}

public sealed record Note(long Id, long? TopicId, string Kind, string Body, DateTime CreatedUtc);

public enum TopicScope { Open, Waiting, Review, Paused, Closed, All }

/// <summary>Topics followed over time: status, who we wait on, next review date, a timeline of notes, and the
/// tasks and links that belong to them. Status changes write their own line in the timeline.</summary>
public static class Topics
{
    const string Select = """
        SELECT tp.id, tp.section_id, tp.title, tp.description, tp.status, tp.waiting_on_person_id, p.name AS waiting_on,
               tp.waiting_since_utc, tp.next_review_date, tp.created_utc, tp.updated_utc, tp.closed_utc, s.name AS section_name,
               (SELECT group_concat(g.name, char(31)) FROM topic_tag x JOIN tag g ON g.id = x.tag_id WHERE x.topic_id = tp.id) AS tags,
               (SELECT count(*) FROM task t WHERE t.topic_id = tp.id AND t.status = 'open' AND t.deleted_utc IS NULL AND t.parent_task_id IS NULL) AS open_tasks,
               (SELECT count(*) FROM note n WHERE n.topic_id = tp.id AND n.deleted_utc IS NULL AND n.kind = 'note') AS notes,
               (SELECT max(n.created_utc) FROM note n WHERE n.topic_id = tp.id AND n.deleted_utc IS NULL) AS last_note
          FROM topic tp
          LEFT JOIN person p  ON p.id = tp.waiting_on_person_id
          LEFT JOIN section s ON s.id = tp.section_id
        """;

    static Topic Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), SectionId = Db.Long(r, "section_id"), Title = r.GetString(2), Description = Db.Str(r, "description"),
        Status = Topic.FromDb(r.GetString(4)), WaitingOnId = Db.Long(r, "waiting_on_person_id"), WaitingOn = Db.Str(r, "waiting_on"),
        WaitingSinceUtc = Db.UtcOrNull(r, "waiting_since_utc"), NextReview = Db.DateOrNull(r, "next_review_date"),
        CreatedUtc = Db.ParseUtc(r.GetString(9)), UpdatedUtc = Db.ParseUtc(r.GetString(10)), ClosedUtc = Db.UtcOrNull(r, "closed_utc"),
        SectionName = Db.Str(r, "section_name"),
        Tags = Db.Str(r, "tags") is string tags ? tags.Split('\u001f').OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() : [],
        OpenTasks = (int)r.GetInt64(14), Notes = (int)r.GetInt64(15), LastNoteUtc = Db.UtcOrNull(r, "last_note"),
    };

    public static List<Topic> List(TopicScope scope, DateOnly today, long? sectionId = null, string? text = null) => Repo.Read(c =>
    {
        var sql = new StringBuilder(Select).Append(" WHERE tp.deleted_utc IS NULL");
        var args = new List<(string, object?)> { ("$today", Db.Date(today)) };
        sql.Append(scope switch
        {
            TopicScope.Open => " AND tp.status <> 'closed'",
            TopicScope.Waiting => " AND tp.status = 'waiting'",
            TopicScope.Review => " AND tp.status <> 'closed' AND tp.next_review_date <= $today",
            TopicScope.Paused => " AND tp.status = 'paused'",
            TopicScope.Closed => " AND tp.status = 'closed'",
            _ => "",
        });
        if (sectionId is long sid) { sql.Append(" AND (tp.section_id = $sid OR tp.section_id IN (SELECT id FROM section WHERE parent_id = $sid))"); args.Add(("$sid", sid)); }
        if (!string.IsNullOrWhiteSpace(text)) { sql.Append(" AND (tp.title LIKE $text ESCAPE '\\' OR tp.description LIKE $text ESCAPE '\\')"); args.Add(("$text", "%" + Tasks.Like(text.Trim()) + "%")); }
        sql.Append(scope == TopicScope.Closed
            ? " ORDER BY tp.closed_utc DESC"
            // Due for review first, then by review date, then the ones touched most recently.
            : " ORDER BY (tp.next_review_date <= $today) DESC, tp.next_review_date IS NULL, tp.next_review_date, tp.updated_utc DESC");
        return Db.Query(c, sql.ToString(), Map, args.ToArray());
    });

    public static Topic? Get(long id) => Repo.Read(c => Get(c, id));
    static Topic? Get(SqliteConnection c, long id) => Db.Query(c, Select + " WHERE tp.id = $id", Map, ("$id", id)).FirstOrDefault();

    public static (int review, int waiting, int open) Counts(DateOnly today) => Repo.Read(c =>
    {
        using var cmd = Db.Cmd(c, """
            SELECT coalesce(sum(next_review_date <= $today), 0), coalesce(sum(status = 'waiting'), 0), count(*)
              FROM topic WHERE status <> 'closed' AND deleted_utc IS NULL
            """, ("$today", Db.Date(today)));
        using var r = cmd.ExecuteReader();
        r.Read();
        return ((int)r.GetInt64(0), (int)r.GetInt64(1), (int)r.GetInt64(2));
    });

    public static long Add(string title, long? sectionId = null, DateOnly? nextReview = null, IReadOnlyList<string>? tags = null) => Repo.Write("topic", (c, _) =>
    {
        string now = Db.NowUtc();
        long id = Db.Insert(c, "INSERT INTO topic(section_id, title, next_review_date, created_utc, updated_utc) VALUES ($s, $t, $r, $now, $now)",
            ("$s", sectionId), ("$t", title.Trim()), ("$r", Db.Value(nextReview)), ("$now", now));
        if (tags is { Count: > 0 }) SetTags(c, id, tags);
        return id;
    });

    /// <summary>Saves title, description, area, review date and tags (status has its own method).</summary>
    public static void Update(Topic t, IReadOnlyList<string>? tags = null) => Repo.Write("topic", (c, _) =>
    {
        Db.Exec(c, "UPDATE topic SET title = $t, description = $d, section_id = $s, next_review_date = $r, updated_utc = $now WHERE id = $id",
            ("$t", t.Title.Trim()), ("$d", string.IsNullOrWhiteSpace(t.Description) ? null : t.Description.Trim()), ("$s", t.SectionId),
            ("$r", Db.Value(t.NextReview)), ("$now", Db.NowUtc()), ("$id", t.Id));
        if (tags != null) SetTags(c, t.Id, tags);
    });

    /// <summary>Changes the status (waiting needs a person) and writes the change in the timeline with
    /// <paramref name="noteText"/> (already in the person's language).</summary>
    public static void SetStatus(long id, TopicState status, string? waitingOn, string noteText) => Repo.Write("topic", (c, _) =>
    {
        var current = Get(c, id) ?? throw new InvalidOperationException("Topic not found: " + id);
        long? personId = null;
        if (status == TopicState.Waiting)
        {
            if (string.IsNullOrWhiteSpace(waitingOn)) throw new ArgumentException("Waiting needs a person.", nameof(waitingOn));
            personId = People.Ensure(c, waitingOn);
        }
        if (current.Status == status && current.WaitingOnId == personId) return;
        string now = Db.NowUtc();
        bool stillWaitingSame = status == TopicState.Waiting && current.Status == TopicState.Waiting && current.WaitingOnId == personId;
        Db.Exec(c, """
            UPDATE topic SET status = $st, waiting_on_person_id = $p, waiting_since_utc = $since, closed_utc = $closed, updated_utc = $now
             WHERE id = $id
            """, ("$st", Topic.ToDb(status)), ("$p", personId),
            ("$since", status == TopicState.Waiting ? (stillWaitingSame ? Db.Value(current.WaitingSinceUtc) : now) : null),
            ("$closed", status == TopicState.Closed ? now : null), ("$now", now), ("$id", id));
        Db.Exec(c, "INSERT INTO note(topic_id, kind, body, created_utc) VALUES ($t, 'status', $b, $now)", ("$t", id), ("$b", noteText), ("$now", now));
    });

    /// <summary>"Reviewed": sets the next review date and notes it in the timeline.</summary>
    public static void Reviewed(long id, DateOnly? next, string noteText) => Repo.Write("topic", (c, _) =>
    {
        string now = Db.NowUtc();
        Db.Exec(c, "UPDATE topic SET next_review_date = $r, updated_utc = $now WHERE id = $id", ("$r", Db.Value(next)), ("$now", now), ("$id", id));
        Db.Exec(c, "INSERT INTO note(topic_id, kind, body, created_utc) VALUES ($t, 'system', $b, $now)", ("$t", id), ("$b", noteText), ("$now", now));
    });

    public static void Delete(long id) => Repo.Write("topic", (c, _) => Db.Exec(c, "UPDATE topic SET deleted_utc = $now WHERE id = $id", ("$now", Db.NowUtc()), ("$id", id)));
    public static void Restore(long id) => Repo.Write("topic", (c, _) => Db.Exec(c, "UPDATE topic SET deleted_utc = NULL WHERE id = $id", ("$id", id)));

    static void SetTags(SqliteConnection c, long topicId, IEnumerable<string> names)
    {
        var ids = Data.Tags.Ensure(c, names);
        Db.Exec(c, "DELETE FROM topic_tag WHERE topic_id = $t", ("$t", topicId));
        foreach (long id in ids) Db.Exec(c, "INSERT INTO topic_tag(topic_id, tag_id) VALUES ($t, $g)", ("$t", topicId), ("$g", id));
        Data.Tags.PruneUnused(c);
    }

    // ------------------------------------------------------------------ timeline
    public static List<Note> Timeline(long topicId) => Repo.Read(c => Db.Query(c,
        "SELECT id, topic_id, kind, body, created_utc FROM note WHERE topic_id = $t AND deleted_utc IS NULL ORDER BY created_utc DESC, id DESC",
        r => new Note(r.GetInt64(0), Db.Long(r, "topic_id"), r.GetString(2), r.GetString(3), Db.ParseUtc(r.GetString(4))), ("$t", topicId)));

    /// <summary>A note in a topic's timeline, or a loose note (inbox) when <paramref name="topicId"/> is null.</summary>
    public static long AddNote(long? topicId, string body) => Repo.Write(topicId == null ? "note" : "topic", (c, _) =>
    {
        string now = Db.NowUtc();
        long id = Db.Insert(c, "INSERT INTO note(topic_id, kind, body, created_utc) VALUES ($t, 'note', $b, $now)", ("$t", topicId), ("$b", body.Trim()), ("$now", now));
        if (topicId != null) Db.Exec(c, "UPDATE topic SET updated_utc = $now WHERE id = $t", ("$now", now), ("$t", topicId));
        return id;
    });

    public static void DeleteNote(long id) => Repo.Write("topic", (c, _) => Db.Exec(c, "UPDATE note SET deleted_utc = $now WHERE id = $id", ("$now", Db.NowUtc()), ("$id", id)));
    public static void RestoreNote(long id) => Repo.Write("topic", (c, _) => Db.Exec(c, "UPDATE note SET deleted_utc = NULL WHERE id = $id", ("$id", id)));

    /// <summary>Loose notes (quick capture) not yet filed in a topic.</summary>
    public static List<Note> Inbox() => Repo.Read(c => Db.Query(c,
        "SELECT id, topic_id, kind, body, created_utc FROM note WHERE topic_id IS NULL AND deleted_utc IS NULL ORDER BY created_utc DESC",
        r => new Note(r.GetInt64(0), null, r.GetString(2), r.GetString(3), Db.ParseUtc(r.GetString(4)))));

    /// <summary>Files a loose note into a topic's timeline.</summary>
    public static void FileNote(long noteId, long topicId) => Repo.Write("topic", (c, _) =>
        Db.Exec(c, "UPDATE note SET topic_id = $t WHERE id = $id AND topic_id IS NULL", ("$t", topicId), ("$id", noteId)));
}

public static class People
{
    public static List<Person> All() => Repo.Read(c => Db.Query(c, "SELECT id, name, email FROM person ORDER BY name COLLATE NOCASE",
        r => new Person(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2))));

    /// <summary>The person's id, creating them if needed (names are unique, case-insensitive).</summary>
    public static long Ensure(SqliteConnection c, string name)
    {
        name = name.Trim();
        Db.Exec(c, "INSERT INTO person(name, created_utc) VALUES ($n, $now) ON CONFLICT(name) DO NOTHING", ("$n", name), ("$now", Db.NowUtc()));
        return Db.Scalar<long>(c, "SELECT id FROM person WHERE name = $n", ("$n", name));
    }
}
