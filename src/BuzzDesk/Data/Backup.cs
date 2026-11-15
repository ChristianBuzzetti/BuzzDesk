using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>
/// Online backups with SQLite's backup API (consistent while the app keeps working), verified with quick_check.
/// Daily: buzzdesk-YYYYMMDD.db, the first time the app runs each day (or at the day change while it sits in the tray).
/// Retention: the newest <c>keepDaily</c> daily copies, plus the oldest copy of each ISO week for <c>keepWeekly</c>
/// weeks. Copies made before a migration (…-pre-migration.db) keep the last 5.
/// </summary>
public static class Backup
{
    public const string Prefix = "buzzdesk-";

    public sealed record Result(string Path, long Bytes, bool Verified);

    /// <summary>Copies the open database to DIR\buzzdesk-YYYYMMDD[-HHmmss-tag].db and verifies the copy.</summary>
    public static Result Run(SqliteConnection source, string dir, string? tag = null, DateTime? localNow = null)
    {
        Directory.CreateDirectory(dir);
        var now = localNow ?? DateTime.Now;
        string name = Prefix + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + (tag == null ? "" : "-" + now.ToString("HHmmss", CultureInfo.InvariantCulture) + "-" + tag) + ".db";
        string path = Path.Combine(dir, name);
        string staging = path + ".tmp";
        if (File.Exists(staging)) File.Delete(staging);
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = staging, Pooling = false }.ToString()))
        {
            target.Open();
            source.BackupDatabase(target);
            // A backup is a single file: no WAL beside it.
            Db.Exec(target, "PRAGMA journal_mode = DELETE;");
            string check = Db.Scalar<string>(target, "PRAGMA quick_check;") ?? "";
            if (check != "ok") { target.Close(); File.Delete(staging); throw new InvalidDataException("Backup failed quick_check: " + check); }
        }
        File.Move(staging, path, overwrite: true);
        return new Result(path, new FileInfo(path).Length, true);
    }

    /// <summary>Today's daily backup if it is not there yet; then applies retention. Null when nothing was needed.</summary>
    public static Result? DailyIfDue(string dir, int keepDaily, int keepWeekly, DateTime? localNow = null)
    {
        var now = localNow ?? DateTime.Now;
        string today = Path.Combine(dir, Prefix + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".db");
        if (File.Exists(today)) return null;
        Result r;
        using (var c = Db.Open())
        {
            r = Run(c, dir, null, now);
            Db.SetMeta(c, "last_backup_utc", Db.NowUtc());
        }
        Prune(dir, keepDaily, keepWeekly);
        return r;
    }

    /// <summary>Deletes daily copies beyond the retention and old pre-migration copies. Returns the files removed.</summary>
    public static List<string> Prune(string dir, int keepDaily, int keepWeekly)
    {
        var removed = new List<string>();
        if (!Directory.Exists(dir)) return removed;
        var daily = new List<(DateOnly day, string path)>();
        var premigration = new List<string>();
        foreach (var f in Directory.GetFiles(dir, Prefix + "*.db"))
        {
            string n = Path.GetFileNameWithoutExtension(f)[Prefix.Length..];
            if (n.Length == 8 && DateOnly.TryParseExact(n, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) daily.Add((d, f));
            else if (n.EndsWith("-pre-migration", StringComparison.Ordinal)) premigration.Add(f);
        }
        daily.Sort((a, b) => b.day.CompareTo(a.day));   // newest first
        var keep = new HashSet<string>(daily.Take(Math.Max(1, keepDaily)).Select(x => x.path));
        if (daily.Count > 0 && keepWeekly > 0)
        {
            var newestWeek = WeekStart(daily[0].day);
            foreach (var g in daily.GroupBy(x => WeekStart(x.day)))
            {
                int weeksAgo = (newestWeek.DayNumber - g.Key.DayNumber) / 7;
                if (weeksAgo < keepWeekly) keep.Add(g.OrderBy(x => x.day).First().path);   // the week's oldest copy
            }
        }
        foreach (var (_, path) in daily)
            if (!keep.Contains(path)) { TryDelete(path); removed.Add(path); }
        foreach (var path in premigration.OrderByDescending(p => p, StringComparer.Ordinal).Skip(5)) { TryDelete(path); removed.Add(path); }
        return removed;
    }

    static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));   // Monday

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
