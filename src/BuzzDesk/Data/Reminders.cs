using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>A reminder with what it points at (task, topic or its own title).</summary>
public sealed record Reminder(long Id, long? TaskId, long? TopicId, string Title, DateTime RemindAtUtc, DateTime? SnoozedUntilUtc,
    DateTime? FiredUtc, DateTime? DismissedUtc, DateTime CreatedUtc, DateOnly? TaskDue, bool TaskOpen)
{
    /// <summary>When it rings: the snooze time if snoozed, else the planned time.</summary>
    public DateTime DueUtc => SnoozedUntilUtc ?? RemindAtUtc;
    public bool Pending => DismissedUtc == null;
    public string Kind => TaskId != null ? "task" : TopicId != null ? "topic" : "loose";
}

/// <summary>
/// Reminders. Life cycle: pending (dismissed_utc NULL) → fired (fired_utc set when the toast is shown) → dismissed.
/// Snoozing sets snoozed_until_utc and clears fired_utc, so it rings again. Reminders of a deleted or completed
/// task do not ring.
/// </summary>
public static class Reminders
{
    const string Select = """
        SELECT r.id, r.task_id, r.topic_id,
               coalesce(t.title, tp.title, r.title) AS title,
               r.remind_at_utc, r.snoozed_until_utc, r.fired_utc, r.dismissed_utc, r.created_utc,
               t.due_date, coalesce(t.status = 'open', 1) AS task_open
          FROM reminder r
          LEFT JOIN task t  ON t.id = r.task_id
          LEFT JOIN topic tp ON tp.id = r.topic_id
        """;
    // Rings only while its task is open and not deleted (and its topic not deleted).
    const string Alive = " (r.task_id IS NULL OR (t.status = 'open' AND t.deleted_utc IS NULL)) AND (r.topic_id IS NULL OR tp.deleted_utc IS NULL) ";

    static Reminder Map(SqliteDataReader r) => new(r.GetInt64(0), Db.Long(r, "task_id"), Db.Long(r, "topic_id"), r.IsDBNull(3) ? "" : r.GetString(3),
        Db.ParseUtc(r.GetString(4)), Db.UtcOrNull(r, "snoozed_until_utc"), Db.UtcOrNull(r, "fired_utc"), Db.UtcOrNull(r, "dismissed_utc"),
        Db.ParseUtc(r.GetString(8)), Db.DateOrNull(r, "due_date"), r.GetInt64(10) == 1);

    public static long Add(DateTime remindAtUtc, long? taskId = null, long? topicId = null, string? title = null) => Repo.Write("reminder", (c, _) =>
        Db.Insert(c, "INSERT INTO reminder(task_id, topic_id, title, remind_at_utc, created_utc) VALUES ($t, $tp, $title, $at, $now)",
            ("$t", taskId), ("$tp", topicId), ("$title", string.IsNullOrWhiteSpace(title) ? null : title.Trim()), ("$at", Db.Utc(remindAtUtc)), ("$now", Db.NowUtc())));

    public static Reminder? Get(long id) => Repo.Read(c => Db.Query(c, Select + " WHERE r.id = $id", Map, ("$id", id)).FirstOrDefault());

    /// <summary>Ready to ring now: pending, not shown yet, time reached, owner alive. Oldest first.</summary>
    public static List<Reminder> DueNow(DateTime nowUtc) => Repo.Read(c => Db.Query(c, Select +
        " WHERE r.dismissed_utc IS NULL AND r.fired_utc IS NULL AND coalesce(r.snoozed_until_utc, r.remind_at_utc) <= $now AND" + Alive +
        " ORDER BY coalesce(r.snoozed_until_utc, r.remind_at_utc)", Map, ("$now", Db.Utc(nowUtc))));

    /// <summary>The next time anything rings (for the tray tooltip and the scheduler).</summary>
    public static Reminder? Next(DateTime nowUtc) => Repo.Read(c => Db.Query(c, Select +
        " WHERE r.dismissed_utc IS NULL AND r.fired_utc IS NULL AND coalesce(r.snoozed_until_utc, r.remind_at_utc) > $now AND" + Alive +
        " ORDER BY coalesce(r.snoozed_until_utc, r.remind_at_utc) LIMIT 1", Map, ("$now", Db.Utc(nowUtc))).FirstOrDefault());

    /// <summary>Pending reminders that ring between two instants (a local day), fired ones included.</summary>
    public static List<Reminder> Between(DateTime fromUtc, DateTime toUtc) => Repo.Read(c => Db.Query(c, Select +
        " WHERE r.dismissed_utc IS NULL AND coalesce(r.snoozed_until_utc, r.remind_at_utc) >= $from AND coalesce(r.snoozed_until_utc, r.remind_at_utc) < $to AND" + Alive +
        " ORDER BY coalesce(r.snoozed_until_utc, r.remind_at_utc)", Map, ("$from", Db.Utc(fromUtc)), ("$to", Db.Utc(toUtc))));

    /// <summary>Fired before <paramref name="beforeUtc"/> and never dismissed (missed while away).</summary>
    public static List<Reminder> Unanswered(DateTime beforeUtc) => Repo.Read(c => Db.Query(c, Select +
        " WHERE r.dismissed_utc IS NULL AND r.fired_utc IS NOT NULL AND coalesce(r.snoozed_until_utc, r.remind_at_utc) < $before AND" + Alive +
        " ORDER BY coalesce(r.snoozed_until_utc, r.remind_at_utc)", Map, ("$before", Db.Utc(beforeUtc))));

    public static List<Reminder> ForTask(long taskId) => Repo.Read(c => Db.Query(c, Select +
        " WHERE r.task_id = $t AND r.dismissed_utc IS NULL ORDER BY coalesce(r.snoozed_until_utc, r.remind_at_utc)", Map, ("$t", taskId)));

    public static List<Reminder> ForTopic(long topicId) => Repo.Read(c => Db.Query(c, Select +
        " WHERE r.topic_id = $t AND r.dismissed_utc IS NULL ORDER BY coalesce(r.snoozed_until_utc, r.remind_at_utc)", Map, ("$t", topicId)));

    public static void MarkFired(IEnumerable<long> ids, DateTime nowUtc) => Repo.Write("reminder", (c, _) =>
    {
        foreach (long id in ids) Db.Exec(c, "UPDATE reminder SET fired_utc = $now WHERE id = $id", ("$now", Db.Utc(nowUtc)), ("$id", id));
    });

    public static void Snooze(long id, DateTime untilUtc) => Repo.Write("reminder", (c, _) =>
        Db.Exec(c, "UPDATE reminder SET snoozed_until_utc = $u, fired_utc = NULL, dismissed_utc = NULL WHERE id = $id", ("$u", Db.Utc(untilUtc)), ("$id", id)));

    public static void Dismiss(long id) => Repo.Write("reminder", (c, _) =>
        Db.Exec(c, "UPDATE reminder SET dismissed_utc = coalesce(dismissed_utc, $now) WHERE id = $id", ("$now", Db.NowUtc()), ("$id", id)));

    public static void Delete(long id) => Repo.Write("reminder", (c, _) => Db.Exec(c, "DELETE FROM reminder WHERE id = $id", ("$id", id)));

    /// <summary>Moves a reminder to a new time (it rings again).</summary>
    public static void Reschedule(long id, DateTime atUtc) => Repo.Write("reminder", (c, _) =>
        Db.Exec(c, "UPDATE reminder SET remind_at_utc = $at, snoozed_until_utc = NULL, fired_utc = NULL, dismissed_utc = NULL WHERE id = $id", ("$at", Db.Utc(atUtc)), ("$id", id)));
}
