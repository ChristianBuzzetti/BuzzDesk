using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>A schema change: Data/Migrations/NNNN_description.sql, embedded in the assembly.</summary>
public sealed record Migration(int Version, string Description, string Sql)
{
    /// <summary>SHA-256 of the script (line endings normalized), recorded when applied.</summary>
    public string Checksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sql.Replace("\r\n", "\n")))).ToLowerInvariant();
}

/// <summary>
/// Applies the embedded migrations in order, each in its own transaction, and records them in schema_version
/// (version, description, checksum, applied_utc). An applied migration whose script changed afterwards stops the app:
/// the database and the code disagree and nothing is written.
/// </summary>
public static partial class Migrator
{
    const string VersionTable = """
        CREATE TABLE IF NOT EXISTS schema_version (
          version     INTEGER PRIMARY KEY,
          description TEXT NOT NULL,
          checksum    TEXT NOT NULL CHECK (length(checksum) = 64),
          applied_utc TEXT NOT NULL CHECK (applied_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z')
        ) STRICT;
        """;

    [GeneratedRegex(@"(\d{4})_([A-Za-z0-9_]+)\.sql$")]
    private static partial Regex ScriptName();

    static IReadOnlyList<Migration>? all;
    /// <summary>Every migration shipped with this build, by version.</summary>
    public static IReadOnlyList<Migration> All => all ??= Load();

    static List<Migration> Load()
    {
        var asm = typeof(Migrator).Assembly;
        var list = new List<Migration>();
        foreach (string name in asm.GetManifestResourceNames())
        {
            var m = ScriptName().Match(name);
            if (!m.Success || !name.Contains("Migrations", StringComparison.Ordinal)) continue;
            using var s = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(s, Encoding.UTF8);
            list.Add(new Migration(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Replace('_', ' '), reader.ReadToEnd()));
        }
        list.Sort((a, b) => a.Version.CompareTo(b.Version));
        for (int i = 0; i < list.Count; i++)
            if (list[i].Version != i + 1) throw new InvalidOperationException($"Migrations must be numbered 1, 2, 3… without gaps (found {list[i].Version} at position {i + 1}).");
        return list;
    }

    public static int CurrentVersion(SqliteConnection c)
    {
        Db.Exec(c, VersionTable);
        return (int)(Db.Scalar<long?>(c, "SELECT max(version) FROM schema_version") ?? 0);
    }

    /// <summary>Migrations not applied yet. Throws if an applied one changed or the database is newer than the app.</summary>
    public static IReadOnlyList<Migration> Pending(SqliteConnection c)
    {
        Db.Exec(c, VersionTable);
        var applied = Db.Query(c, "SELECT version, checksum FROM schema_version ORDER BY version", r => (v: (int)r.GetInt64(0), sum: r.GetString(1)));
        foreach (var (v, sum) in applied)
        {
            var m = All.FirstOrDefault(x => x.Version == v)
                ?? throw new InvalidOperationException($"The database is at schema version {applied.Max(a => a.v)}, newer than this BuzzDesk ({All.Count}). Update BuzzDesk.");
            if (m.Checksum != sum) throw new InvalidOperationException($"Migration {v} ({m.Description}) changed after it was applied (checksum mismatch).");
        }
        int current = applied.Count == 0 ? 0 : applied.Max(a => a.v);
        return All.Where(m => m.Version > current).ToList();
    }

    public static int Migrate(SqliteConnection c)
    {
        int count = 0;
        foreach (var m in Pending(c))
        {
            using var tx = Db.Begin(c);
            Db.Exec(c, m.Sql);
            Db.Exec(c, "INSERT INTO schema_version(version, description, checksum, applied_utc) VALUES ($v, $d, $c, $t)",
                ("$v", m.Version), ("$d", m.Description), ("$c", m.Checksum), ("$t", Db.NowUtc()));
            // The whole schema must still hold together after each step.
            using (var check = Db.Cmd(c, "PRAGMA foreign_key_check;"))
            {
                using var r = check.ExecuteReader();
                if (r.Read()) throw new InvalidOperationException($"Migration {m.Version} left foreign key violations (table {r.GetString(0)}).");
            }
            tx.Commit();
            count++;
        }
        return count;
    }
}
