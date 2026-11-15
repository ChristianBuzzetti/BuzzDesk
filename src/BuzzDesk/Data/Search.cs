using System.Text;
using System.Text.RegularExpressions;

namespace BuzzDesk.Data;

/// <summary>One hit: what it is, its id, title and a snippet with the matches between \u0002 and \u0003.</summary>
public sealed record SearchHit(string Kind, long RefId, string Title, string Snippet, double Rank, long? TopicId = null);

/// <summary>Global search (Ctrl+F) over the FTS5 index of migration 0002.</summary>
public static partial class Search
{
    public const char MarkStart = '\u0002', MarkEnd = '\u0003';

    [GeneratedRegex("\"([^\"]+)\"|(\\S+)")] private static partial Regex Terms();

    /// <summary>
    /// The person's text as an FTS5 query: every word must appear (AND), each word also matches as a prefix
    /// ("migra" finds "migración"), "quoted phrases" stay together. FTS syntax typed by the person is neutralised:
    /// only letters and digits reach the index. Null when there is nothing to search.
    /// </summary>
    public static string? BuildQuery(string text)
    {
        var parts = new List<string>();
        foreach (Match m in Terms().Matches(text ?? ""))
        {
            if (m.Groups[1].Success)
            {
                var words = Words(m.Groups[1].Value);
                if (words.Count > 0) parts.Add("\"" + string.Join(" ", words) + "\"");
            }
            else
                foreach (var w in Words(m.Groups[2].Value)) parts.Add("\"" + w + "\"*");
        }
        return parts.Count == 0 ? null : string.Join(" AND ", parts);
    }

    static List<string> Words(string s)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        return words;
    }

    /// <summary>Best hits first (bm25 with title 10, body 1, tags 5). Subtasks point at their parent task; notes
    /// carry their topic's title.</summary>
    public static List<SearchHit> Run(string text, int limit = 60)
    {
        string? q = BuildQuery(text);
        if (q == null) return [];
        return Repo.Read(c => Db.Query(c, """
            SELECT f.kind,
                   CASE WHEN f.kind = 'task' THEN coalesce(t.parent_task_id, t.id) ELSE f.ref_id END AS ref_id,
                   CASE f.kind
                     WHEN 'task' THEN coalesce(pt.title || ' › ', '') || t.title
                     WHEN 'note' THEN coalesce(tp.title, '')
                     ELSE f.title END AS title,
                   snippet(search_fts, -1, char(2), char(3), '…', 14) AS snip,
                   bm25(search_fts, 0, 0, 10.0, 1.0, 5.0) AS rank,
                   CASE f.kind WHEN 'task' THEN t.status = 'open' WHEN 'topic' THEN tpc.status <> 'closed' ELSE 1 END AS live,
                   n.topic_id
              FROM search_fts f
              LEFT JOIN task t    ON f.kind = 'task'  AND t.id = f.ref_id
              LEFT JOIN task pt   ON pt.id = t.parent_task_id
              LEFT JOIN note n    ON f.kind = 'note'  AND n.id = f.ref_id
              LEFT JOIN topic tp  ON tp.id = n.topic_id
              LEFT JOIN topic tpc ON f.kind = 'topic' AND tpc.id = f.ref_id
             WHERE search_fts MATCH $q
             ORDER BY live DESC, rank
             LIMIT $limit
            """, r => new SearchHit(r.GetString(0), r.GetInt64(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3), r.GetDouble(4), r.IsDBNull(6) ? null : r.GetInt64(6)),
            ("$q", q), ("$limit", limit)));
    }
}
