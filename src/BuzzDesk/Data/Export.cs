using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>
/// Everything to CSV files that Excel opens as they are: UTF-8 with BOM, one file per kind, the separator of the
/// person's list format (";" for Spanish Excel, "," for English), dates as yyyy-MM-dd, instants in local time and
/// money as a plain decimal with its currency in its own column.
/// </summary>
public static class Export
{
    public sealed record Options(char Separator, Func<DateTime, DateTime> ToLocal, string DecimalSeparator = ".");

    public static List<string> All(string folder, Options o)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        using var c = Db.Open();
        void File(string name, string sql) => written.Add(Write(c, Path.Combine(folder, name), sql, o));
        File("tasks.csv", """
            SELECT t.id, p.title AS parent, t.title, s.name AS area, tp.title AS topic, t.priority, t.due_date, t.due_time, t.status,
                   t.completed_utc, t.postpone_count, r.freq AS repeat, r.interval AS every,
                   (SELECT group_concat(g.name, ' ') FROM task_tag x JOIN tag g ON g.id = x.tag_id WHERE x.task_id = t.id) AS tags,
                   t.notes, t.created_utc
              FROM task t LEFT JOIN task p ON p.id = t.parent_task_id LEFT JOIN section s ON s.id = t.section_id
              LEFT JOIN topic tp ON tp.id = t.topic_id LEFT JOIN recurrence r ON r.id = t.recurrence_id
             WHERE t.deleted_utc IS NULL ORDER BY coalesce(t.parent_task_id, t.id), t.parent_task_id IS NOT NULL, t.id
            """);
        File("topics.csv", """
            SELECT tp.id, tp.title, s.name AS area, tp.status, p.name AS waiting_on, tp.waiting_since_utc, tp.next_review_date,
                   (SELECT group_concat(g.name, ' ') FROM topic_tag x JOIN tag g ON g.id = x.tag_id WHERE x.topic_id = tp.id) AS tags,
                   tp.description, tp.created_utc, tp.closed_utc
              FROM topic tp LEFT JOIN section s ON s.id = tp.section_id LEFT JOIN person p ON p.id = tp.waiting_on_person_id
             WHERE tp.deleted_utc IS NULL ORDER BY tp.id
            """);
        File("notes.csv", """
            SELECT n.id, tp.title AS topic, n.kind, n.body, n.created_utc
              FROM note n LEFT JOIN topic tp ON tp.id = n.topic_id WHERE n.deleted_utc IS NULL ORDER BY n.created_utc
            """);
        File("links.csv", """
            SELECT l.id, l.title, l.url, l.description, l.pinned, l.open_count,
                   (SELECT group_concat(g.name, ' ') FROM link_tag x JOIN tag g ON g.id = x.tag_id WHERE x.link_id = l.id) AS tags, l.created_utc
              FROM link l WHERE l.deleted_utc IS NULL ORDER BY l.title COLLATE NOCASE
            """);
        File("habit_days.csv", """
            SELECT h.name AS habit, k.day, k.count, h.target_per_day AS target
              FROM habit_check k JOIN habit h ON h.id = k.habit_id ORDER BY h.name, k.day
            """);
        File("bills.csv", """
            SELECT b.id, b.name, b.category, b.kind, b.amount_cents / 100.0 AS amount, b.currency, b.next_due_date,
                   r.freq AS repeat, r.interval AS every, b.remind_days_before, b.notes, b.archived_utc
              FROM bill b LEFT JOIN recurrence r ON r.id = b.recurrence_id ORDER BY b.name COLLATE NOCASE
            """);
        File("payments.csv", """
            SELECT b.name AS bill, p.due_date, p.paid_utc, p.amount_cents / 100.0 AS amount, p.currency, p.note
              FROM bill_payment p JOIN bill b ON b.id = p.bill_id ORDER BY p.due_date, b.name
            """);
        File("lists.csv", """
            SELECT l.name AS list, i.text AS item, i.done_utc IS NOT NULL AS done, i.done_utc
              FROM checklist_item i JOIN checklist l ON l.id = i.checklist_id WHERE l.deleted_utc IS NULL ORDER BY l.name, i.sort_order
            """);
        File("dates.csv", """
            SELECT x.label, x.kind, p.name AS person, x.day, x.month, x.year, x.remind_days_before
              FROM important_date x LEFT JOIN person p ON p.id = x.person_id ORDER BY x.month, x.day
            """);
        return written;
    }

    /// <summary>One query to one CSV. Columns ending in _utc become local "yyyy-MM-dd HH:mm"; REAL columns use the
    /// decimal separator of the export (so Spanish Excel reads 1234,5).</summary>
    static string Write(SqliteConnection c, string path, string sql, Options o)
    {
        using var cmd = Db.Cmd(c, sql);
        using var r = cmd.ExecuteReader();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(o.Separator, Enumerable.Range(0, r.FieldCount).Select(i => Quote(r.GetName(i).Replace("_utc", ""), o.Separator))));
        while (r.Read())
        {
            var cells = new string[r.FieldCount];
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (r.IsDBNull(i)) { cells[i] = ""; continue; }
                string name = r.GetName(i);
                object v = r.GetValue(i);
                string text = v switch
                {
                    string s when name.EndsWith("_utc", StringComparison.Ordinal) && s.Length == 20 => o.ToLocal(Db.ParseUtc(s)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    double d => d.ToString("0.##", CultureInfo.InvariantCulture).Replace(".", o.DecimalSeparator),
                    _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
                };
                cells[i] = Quote(text, o.Separator);
            }
            sb.AppendLine(string.Join(o.Separator, cells));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return path;
    }

    /// <summary>RFC 4180: quote when the text has the separator, quotes or line breaks; a leading = + - @ is
    /// prefixed with ' so Excel never runs it as a formula (CSV injection).</summary>
    public static string Quote(string text, char separator)
    {
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) text = "'" + text;
        return text.IndexOfAny([separator, '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
