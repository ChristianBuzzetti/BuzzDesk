using System.Text;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>
/// One-time data step of the redesign: Topics are gone from the app, so every open topic becomes a task (the way
/// Todoist or TickTick hold the same thing). The task keeps the title, area, review date (as its date), the
/// description, the timeline and the links in its notes, the tags plus "esperando"/"en-pausa" for the status,
/// and the topic's reminders. The topic is closed with a line in its timeline, so nothing is deleted.
/// </summary>
public static class Redesign
{
    const string Done = "topics_converted";

    /// <summary>Returns how many topics became tasks (0 when already done). <paramref name="t"/> translates the
    /// labels written into the notes; <paramref name="backup"/> runs first, only when there is something to convert.</summary>
    public static int ConvertTopics(SqliteConnection c, Func<string, string> t, Action? backup = null)
    {
        if (Db.GetMeta(c, Done) != null) return 0;
        var topics = Db.Query(c, """
            SELECT tp.id, tp.section_id, tp.title, tp.description, tp.status, p.name, tp.next_review_date
              FROM topic tp LEFT JOIN person p ON p.id = tp.waiting_on_person_id
             WHERE tp.deleted_utc IS NULL AND tp.status <> 'closed'
             ORDER BY tp.id
            """, r => (id: r.GetInt64(0), section: Db.Long(r, "section_id"), title: r.GetString(2), description: Db.Str(r, "description"),
                       status: r.GetString(4), waitingOn: r.IsDBNull(5) ? null : r.GetString(5), review: Db.Str(r, "next_review_date")));
        if (topics.Count > 0) backup?.Invoke();
        using var tx = Db.Begin(c);
        string now = Db.NowUtc();
        foreach (var tp in topics)
        {
            var notes = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(tp.description)) notes.AppendLine(tp.description.Trim());
            if (tp.status == "waiting" && tp.waitingOn != null) { if (notes.Length > 0) notes.AppendLine(); notes.AppendLine(string.Format(t("Esperando a {0}"), tp.waitingOn)); }
            var history = Db.Query(c, "SELECT body, created_utc FROM note WHERE topic_id = $t AND deleted_utc IS NULL AND kind = 'note' ORDER BY created_utc, id",
                r => (body: r.GetString(0), at: r.GetString(1)), ("$t", tp.id));
            if (history.Count > 0)
            {
                if (notes.Length > 0) notes.AppendLine();
                notes.AppendLine(t("Historial:"));
                foreach (var (body, at) in history) notes.AppendLine("· " + at[..10] + "  " + body.Trim());
            }
            var links = Db.Query(c, "SELECT l.title, l.url FROM topic_link x JOIN link l ON l.id = x.link_id WHERE x.topic_id = $t AND l.deleted_utc IS NULL ORDER BY l.title",
                r => (title: r.GetString(0), url: r.GetString(1)), ("$t", tp.id));
            if (links.Count > 0)
            {
                if (notes.Length > 0) notes.AppendLine();
                notes.AppendLine(t("Enlaces:"));
                foreach (var (title, url) in links) notes.AppendLine("· " + title + "  " + url);
            }
            int order = (int)(Db.Scalar<long?>(c, "SELECT max(sort_order) FROM task WHERE parent_task_id IS NULL") ?? 0) + 1;
            long task = Db.Insert(c, """
                INSERT INTO task(section_id, title, notes, priority, due_date, sort_order, created_utc, updated_utc)
                VALUES ($s, $title, $notes, 0, $due, $o, $now, $now)
                """, ("$s", tp.section), ("$title", tp.title.Trim()), ("$notes", notes.Length == 0 ? null : notes.ToString().TrimEnd()),
                ("$due", tp.review), ("$o", order), ("$now", now));
            var tags = Db.Query(c, "SELECT g.name FROM topic_tag x JOIN tag g ON g.id = x.tag_id WHERE x.topic_id = $t", r => r.GetString(0), ("$t", tp.id));
            if (tp.status == "waiting") tags.Add(t("esperando"));
            if (tp.status == "paused") tags.Add(t("en-pausa"));
            foreach (long tag in Tags.Ensure(c, tags))
                Db.Exec(c, "INSERT OR IGNORE INTO task_tag(task_id, tag_id) VALUES ($t, $g)", ("$t", task), ("$g", tag));
            Db.Exec(c, "UPDATE reminder SET task_id = $task, topic_id = NULL WHERE topic_id = $t", ("$task", task), ("$t", tp.id));
            Db.Exec(c, "UPDATE topic SET status = 'closed', closed_utc = $now, updated_utc = $now WHERE id = $t", ("$now", now), ("$t", tp.id));
            Db.Exec(c, "INSERT INTO note(topic_id, kind, body, created_utc) VALUES ($t, 'system', $b, $now)", ("$t", tp.id), ("$b", t("Convertido en pendiente")), ("$now", now));
        }
        Db.SetMeta(c, Done, now);
        tx.Commit();
        return topics.Count;
    }
}
