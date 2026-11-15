using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>Areas (Work, Personal) and projects inside them.</summary>
public static class Sections
{
    const string Select = """
        SELECT s.id, s.parent_id, s.name, s.kind, s.color, s.sort_order, s.archived_utc,
               (SELECT count(*) FROM task t
                 WHERE t.section_id = s.id AND t.status = 'open' AND t.deleted_utc IS NULL AND t.parent_task_id IS NULL) AS open_tasks
          FROM section s
        """;

    static Section Map(SqliteDataReader r) => new(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2), r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4), (int)r.GetInt64(5), !r.IsDBNull(6), (int)r.GetInt64(7));

    /// <summary>Areas then their projects, in display order (archived ones only when asked).</summary>
    public static List<Section> All(bool includeArchived = false) => Repo.Read(c => Db.Query(c,
        Select + (includeArchived ? "" : " WHERE s.archived_utc IS NULL") +
        " ORDER BY coalesce((SELECT p.sort_order FROM section p WHERE p.id = s.parent_id), s.sort_order), coalesce(s.parent_id, s.id), s.parent_id IS NOT NULL, s.sort_order, s.name", Map));

    public static Section? Get(long id) => Repo.Read(c => Db.Query(c, Select + " WHERE s.id = $id", Map, ("$id", id)).FirstOrDefault());

    public static long Add(string name, long? parentId)
    {
        name = name.Trim();
        return Repo.Write("section", (c, tx) =>
        {
            int order = (int)(Db.Scalar<long?>(c, "SELECT max(sort_order) FROM section WHERE coalesce(parent_id, 0) = coalesce($p, 0)", ("$p", parentId)) ?? 0) + 1;
            return Db.Insert(c, "INSERT INTO section(parent_id, name, kind, sort_order, created_utc) VALUES ($p, $n, $k, $o, $t)",
                ("$p", parentId), ("$n", name), ("$k", parentId == null ? "area" : "project"), ("$o", order), ("$t", Db.NowUtc()));
        });
    }

    public static void Rename(long id, string name) => Repo.Write("section", (c, _) => Db.Exec(c, "UPDATE section SET name = $n WHERE id = $id", ("$n", name.Trim()), ("$id", id)));

    /// <summary>Archives a section and its projects; their tasks keep the section (and show again if restored).</summary>
    public static void Archive(long id, bool archived) => Repo.Write("section", (c, _) =>
        Db.Exec(c, "UPDATE section SET archived_utc = $t WHERE id = $id OR parent_id = $id", ("$t", archived ? Db.NowUtc() : null), ("$id", id)));

    /// <summary>The first run: Work and Personal areas.</summary>
    public static void SeedDefaults(SqliteConnection c, string work, string personal)
    {
        if (Db.GetMeta(c, "seeded") != null) return;
        if (Db.Scalar<long>(c, "SELECT count(*) FROM section") == 0)
        {
            string now = Db.NowUtc();
            Db.Exec(c, "INSERT INTO section(name, kind, sort_order, created_utc) VALUES ($w, 'area', 1, $t), ($p, 'area', 2, $t)", ("$w", work), ("$p", personal), ("$t", now));
        }
        Db.SetMeta(c, "seeded", Db.NowUtc());
    }
}

public static class Tags
{
    public static List<Tag> All() => Repo.Read(c => Db.Query(c, "SELECT id, name, color FROM tag ORDER BY name COLLATE NOCASE", r => new Tag(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2))));

    /// <summary>Tag ids for the names, creating the missing ones (case-insensitive, '#' and blanks dropped).</summary>
    public static List<long> Ensure(SqliteConnection c, IEnumerable<string> names)
    {
        var ids = new List<long>();
        foreach (var raw in names)
        {
            string name = raw.Trim().TrimStart('#').Trim();
            if (name.Length == 0) continue;
            Db.Exec(c, "INSERT INTO tag(name) VALUES ($n) ON CONFLICT(name) DO NOTHING", ("$n", name));
            long id = Db.Scalar<long>(c, "SELECT id FROM tag WHERE name = $n", ("$n", name));
            if (!ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Tags nobody uses any more.</summary>
    public static int PruneUnused(SqliteConnection c) => Db.Exec(c, """
        DELETE FROM tag WHERE NOT EXISTS (SELECT 1 FROM task_tag  x WHERE x.tag_id = tag.id)
                          AND NOT EXISTS (SELECT 1 FROM topic_tag x WHERE x.tag_id = tag.id)
                          AND NOT EXISTS (SELECT 1 FROM link_tag  x WHERE x.tag_id = tag.id)
        """);
}
