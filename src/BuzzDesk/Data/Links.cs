using System.Text;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

public sealed record Link
{
    public long Id { get; init; }
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public byte[]? Favicon { get; init; }
    public DateTime? FaviconUtc { get; init; }
    public bool Pinned { get; init; }
    public int OpenCount { get; init; }
    public DateTime? LastOpenedUtc { get; init; }
    public DateTime CreatedUtc { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>"grafana.local" for http(s) links, the scheme's own text otherwise.</summary>
    public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var u) ? (u.IsFile ? u.LocalPath : u.Scheme == "mailto" ? u.UserInfo + "@" + u.Host : u.Host) : Url;
}

/// <summary>Useful links: one live row per URL, tags, pinned ones for Today, usage counts for the launcher, and the
/// links that belong to topics.</summary>
public static class Links
{
    const string Select = """
        SELECT l.id, l.url, l.title, l.description, l.favicon, l.favicon_utc, l.pinned, l.open_count, l.last_opened_utc, l.created_utc,
               (SELECT group_concat(g.name, char(31)) FROM link_tag x JOIN tag g ON g.id = x.tag_id WHERE x.link_id = l.id) AS tags
          FROM link l
        """;

    static Link Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), Url = r.GetString(1), Title = r.GetString(2), Description = Db.Str(r, "description"),
        Favicon = r.IsDBNull(4) ? null : (byte[])r.GetValue(4), FaviconUtc = Db.UtcOrNull(r, "favicon_utc"), Pinned = r.GetInt64(6) == 1,
        OpenCount = (int)r.GetInt64(7), LastOpenedUtc = Db.UtcOrNull(r, "last_opened_utc"), CreatedUtc = Db.ParseUtc(r.GetString(9)),
        Tags = Db.Str(r, "tags") is string tags ? tags.Split('\u001f').OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() : [],
    };

    /// <summary>Adds "https://" when the text looks like a host ("grafana.local/d/x"); keeps http, https, file and mailto.</summary>
    public static string? Normalize(string text)
    {
        string t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        if (t.Length > 2 && char.IsLetter(t[0]) && t[1] == ':' && (t[2] == '\\' || t[2] == '/')) return new Uri(t).AbsoluteUri;   // C:\docs\x.pdf
        if (t.StartsWith(@"\\", StringComparison.Ordinal)) return new Uri(t).AbsoluteUri;                                         // \\server\share
        if (!t.Contains("://", StringComparison.Ordinal) && !t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) t = "https://" + t;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "file" or "mailto")) return null;
        return t;
    }

    public static List<Link> List(string? text = null, long? tagId = null, bool pinnedOnly = false, int limit = 1000) => Repo.Read(c =>
    {
        var sql = new StringBuilder(Select).Append(" WHERE l.deleted_utc IS NULL");
        var args = new List<(string, object?)> { ("$limit", limit) };
        if (pinnedOnly) sql.Append(" AND l.pinned = 1");
        if (tagId is long tid) { sql.Append(" AND EXISTS (SELECT 1 FROM link_tag x WHERE x.link_id = l.id AND x.tag_id = $tid)"); args.Add(("$tid", tid)); }
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Every word must appear in the title, URL, description or a tag.
            int i = 0;
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string p = "$w" + i++;
                sql.Append($" AND (l.title LIKE {p} ESCAPE '\\' OR l.url LIKE {p} ESCAPE '\\' OR l.description LIKE {p} ESCAPE '\\'" +
                           $" OR EXISTS (SELECT 1 FROM link_tag x JOIN tag g ON g.id = x.tag_id WHERE x.link_id = l.id AND g.name LIKE {p} ESCAPE '\\'))");
                args.Add((p, "%" + Tasks.Like(word) + "%"));
            }
        }
        sql.Append(" ORDER BY l.pinned DESC, l.title COLLATE NOCASE LIMIT $limit");
        return Db.Query(c, sql.ToString(), Map, args.ToArray());
    });

    /// <summary>Launcher ranking: title starts with the text, then words found; most used and most recent first.</summary>
    public static List<Link> Launch(string text, int limit = 8)
    {
        var all = List(text, limit: 200);
        string t = text.Trim();
        return all.OrderByDescending(l => t.Length > 0 && l.Title.StartsWith(t, StringComparison.CurrentCultureIgnoreCase))
                  .ThenByDescending(l => l.Pinned)
                  .ThenByDescending(l => l.OpenCount)
                  .ThenByDescending(l => l.LastOpenedUtc ?? DateTime.MinValue)
                  .Take(limit).ToList();
    }

    public static int Count() => Repo.Read(c => (int)Db.Scalar<long>(c, "SELECT count(*) FROM link WHERE deleted_utc IS NULL"));

    public static Link? Get(long id) => Repo.Read(c => Db.Query(c, Select + " WHERE l.id = $id", Map, ("$id", id)).FirstOrDefault());
    public static Link? FindByUrl(string url) => Repo.Read(c => Db.Query(c, Select + " WHERE l.url = $u AND l.deleted_utc IS NULL", Map, ("$u", url)).FirstOrDefault());

    /// <summary>Saves a new link; when the URL is already saved, returns that one instead (no duplicates).</summary>
    public static (long id, bool existed) Add(string url, string title, string? description = null, IReadOnlyList<string>? tags = null, bool pinned = false) => Repo.Write("link", (c, _) =>
    {
        if (Db.Scalar<long?>(c, "SELECT id FROM link WHERE url = $u AND deleted_utc IS NULL", ("$u", url)) is long existing) return (existing, true);
        string now = Db.NowUtc();
        long id = Db.Insert(c, "INSERT INTO link(url, title, description, pinned, created_utc, updated_utc) VALUES ($u, $t, $d, $p, $now, $now)",
            ("$u", url), ("$t", string.IsNullOrWhiteSpace(title) ? url : title.Trim()), ("$d", Blank(description)), ("$p", pinned ? 1 : 0), ("$now", now));
        if (tags is { Count: > 0 }) SetTags(c, id, tags);
        return (id, false);
    });

    public static void Update(Link l, IReadOnlyList<string>? tags = null) => Repo.Write("link", (c, _) =>
    {
        Db.Exec(c, "UPDATE link SET url = $u, title = $t, description = $d, pinned = $p, updated_utc = $now WHERE id = $id",
            ("$u", l.Url), ("$t", l.Title.Trim()), ("$d", Blank(l.Description)), ("$p", l.Pinned ? 1 : 0), ("$now", Db.NowUtc()), ("$id", l.Id));
        if (tags != null) SetTags(c, l.Id, tags);
    });

    public static void SetPinned(long id, bool pinned) => Repo.Write("link", (c, _) =>
        Db.Exec(c, "UPDATE link SET pinned = $p, updated_utc = $now WHERE id = $id", ("$p", pinned ? 1 : 0), ("$now", Db.NowUtc()), ("$id", id)));

    /// <summary>Counts an open (for the launcher's ranking).</summary>
    public static void Opened(long id) => Repo.Write("link", (c, _) =>
        Db.Exec(c, "UPDATE link SET open_count = open_count + 1, last_opened_utc = $now WHERE id = $id", ("$now", Db.NowUtc()), ("$id", id)));

    /// <summary>Stores the site icon (or marks the attempt with no icon, so it is not retried for a while).</summary>
    public static void SetFavicon(long id, byte[]? icon) => Repo.Write("link", (c, _) =>
        Db.Exec(c, "UPDATE link SET favicon = $f, favicon_utc = $now WHERE id = $id", ("$f", icon), ("$now", Db.NowUtc()), ("$id", id)));

    public static void Delete(long id) => Repo.Write("link", (c, _) => Db.Exec(c, "UPDATE link SET deleted_utc = $now WHERE id = $id", ("$now", Db.NowUtc()), ("$id", id)));

    /// <summary>Back from the trash, unless the same URL was saved again meanwhile (then false).</summary>
    public static bool Restore(long id) => Repo.Write("link", (c, _) =>
    {
        try { return Db.Exec(c, "UPDATE link SET deleted_utc = NULL WHERE id = $id", ("$id", id)) == 1; }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return false; }
    });

    static void SetTags(SqliteConnection c, long linkId, IEnumerable<string> names)
    {
        var ids = Data.Tags.Ensure(c, names);
        Db.Exec(c, "DELETE FROM link_tag WHERE link_id = $l", ("$l", linkId));
        foreach (long id in ids) Db.Exec(c, "INSERT INTO link_tag(link_id, tag_id) VALUES ($l, $g)", ("$l", linkId), ("$g", id));
        Data.Tags.PruneUnused(c);
    }

    // ------------------------------------------------------------------ topics
    public static List<Link> ForTopic(long topicId) => Repo.Read(c => Db.Query(c,
        Select + " JOIN topic_link tl ON tl.link_id = l.id WHERE tl.topic_id = $t AND l.deleted_utc IS NULL ORDER BY l.title COLLATE NOCASE", Map, ("$t", topicId)));

    public static void Attach(long topicId, long linkId) => Repo.Write("topic", (c, _) =>
        Db.Exec(c, "INSERT INTO topic_link(topic_id, link_id) VALUES ($t, $l) ON CONFLICT DO NOTHING", ("$t", topicId), ("$l", linkId)));

    public static void Detach(long topicId, long linkId) => Repo.Write("topic", (c, _) =>
        Db.Exec(c, "DELETE FROM topic_link WHERE topic_id = $t AND link_id = $l", ("$t", topicId), ("$l", linkId)));

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
