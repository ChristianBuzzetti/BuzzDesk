using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>
/// Backups to a folder the person chooses, normally one their cloud client syncs (OneDrive, Google Drive, Dropbox,
/// iCloud, a NAS). Only verified copies go there, never the live database (a WAL database in a synced folder gets
/// corrupted). Optional encryption: AES-256-GCM with a key from the passphrase (PBKDF2-SHA256, 600 000 rounds).
/// File: BuzzDesk-YYYYMMDD-HHmmss.bzdk (encrypted) or .db (plain). Retention: the newest copy of each of the last
/// <c>keepDaily</c> days, plus the oldest of each of the last <c>keepMonthly</c> months.
/// </summary>
public static class CloudBackup
{
    public const string Prefix = "BuzzDesk-";
    static readonly byte[] Magic = "BZDK1"u8.ToArray();
    const int SaltSize = 16, NonceSize = 12, TagSize = 16, Iterations = 600_000;

    public sealed record Result(string Path, long Bytes, bool Encrypted);
    public sealed record Inspection(string TempDb, int SchemaVersion, DateTime? CreatedLocal, int Tasks, int Topics, int Links, int Habits, int Bills);

    // ------------------------------------------------------------------ write
    /// <summary>A verified backup of the open database into <paramref name="folder"/> (encrypted when a passphrase
    /// is given), then retention. The file appears atomically, so a sync client never uploads half a copy.</summary>
    public static Result Run(SqliteConnection source, string folder, string? passphrase, DateTime localNow, int keepDaily = 30, int keepMonthly = 12)
    {
        Directory.CreateDirectory(folder);
        string work = Path.Combine(Path.GetTempPath(), "buzzdesk-cloud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var local = Backup.Run(source, work, "cloud", localNow);   // verified with quick_check
            SqliteConnection.ClearPool(new SqliteConnection("Data Source=" + local.Path));
            byte[] data = File.ReadAllBytes(local.Path);
            bool encrypt = !string.IsNullOrEmpty(passphrase);
            if (encrypt) data = Encrypt(data, passphrase!);
            string name = Prefix + localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + (encrypt ? ".bzdk" : ".db");
            string target = Path.Combine(folder, name), staging = Path.Combine(folder, "." + name + ".partial");
            File.WriteAllBytes(staging, data);
            File.Move(staging, target, overwrite: true);
            Prune(folder, keepDaily, keepMonthly);
            return new Result(target, data.Length, encrypt);
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public static byte[] Encrypt(byte[] plain, string passphrase)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize), nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, plain, cipher, tag, Magic);
        using var ms = new MemoryStream();
        ms.Write(Magic); ms.Write(salt); ms.Write(BitConverter.GetBytes(Iterations)); ms.Write(nonce); ms.Write(tag); ms.Write(cipher);
        return ms.ToArray();
    }

    /// <summary>Decrypts a .bzdk; throws <see cref="CryptographicException"/> on a wrong passphrase or a damaged file.</summary>
    public static byte[] Decrypt(byte[] file, string passphrase)
    {
        int header = Magic.Length + SaltSize + 4 + NonceSize + TagSize;
        if (file.Length < header || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Not a BuzzDesk encrypted backup.");
        int o = Magic.Length;
        var salt = file.AsSpan(o, SaltSize).ToArray(); o += SaltSize;
        int iterations = BitConverter.ToInt32(file, o); o += 4;
        if (iterations is < 100_000 or > 10_000_000) throw new InvalidDataException("Unexpected key derivation settings.");
        var nonce = file.AsSpan(o, NonceSize).ToArray(); o += NonceSize;
        var tag = file.AsSpan(o, TagSize).ToArray(); o += TagSize;
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[file.Length - o];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, file.AsSpan(o), tag, plain, Magic);
        return plain;
    }

    public static bool IsEncrypted(string path) => path.EndsWith(".bzdk", StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies in a folder, newest first, with the moment in their name.</summary>
    public static List<(string path, DateTime when, long bytes)> List(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var list = new List<(string, DateTime, long)>();
        foreach (var f in Directory.GetFiles(folder, Prefix + "*.*"))
        {
            string n = Path.GetFileNameWithoutExtension(f)[Prefix.Length..];
            if (!f.EndsWith(".bzdk", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) continue;
            if (DateTime.TryParseExact(n, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)) list.Add((f, when, new FileInfo(f).Length));
        }
        return list.OrderByDescending(x => x.Item2).ToList();
    }

    public static List<string> Prune(string folder, int keepDaily, int keepMonthly)
    {
        var all = List(folder);
        var keep = new HashSet<string>();
        foreach (var day in all.GroupBy(x => x.when.Date).OrderByDescending(g => g.Key).Take(Math.Max(1, keepDaily)))
            keep.Add(day.OrderByDescending(x => x.when).First().path);                    // newest of each recent day
        foreach (var month in all.GroupBy(x => (x.when.Year, x.when.Month)).OrderByDescending(g => g.Key).Take(Math.Max(0, keepMonthly)))
            keep.Add(month.OrderBy(x => x.when).First().path);                           // oldest of each recent month
        var removed = new List<string>();
        foreach (var (path, _, _) in all)
            if (!keep.Contains(path)) { try { File.Delete(path); removed.Add(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        return removed;
    }

    // ------------------------------------------------------------------ restore
    /// <summary>Opens a copy (decrypting it to a temp file) and checks it is a sound BuzzDesk database this version
    /// can open: application_id, quick_check, schema version. Throws with a readable reason otherwise.</summary>
    public static Inspection Inspect(string file, string? passphrase)
    {
        byte[] bytes = File.ReadAllBytes(file);
        if (IsEncrypted(file))
        {
            if (string.IsNullOrEmpty(passphrase)) throw new CryptographicException("This copy is encrypted: the passphrase is needed.");
            bytes = Decrypt(bytes, passphrase);
        }
        string temp = Path.Combine(Path.GetTempPath(), "buzzdesk-restore-" + Guid.NewGuid().ToString("N") + ".db");
        File.WriteAllBytes(temp, bytes);
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            if (Db.Scalar<long>(c, "PRAGMA application_id;") != Db.ApplicationId) throw new InvalidDataException("The file is not a BuzzDesk database.");
            if (Db.Scalar<string>(c, "PRAGMA quick_check;") != "ok") throw new InvalidDataException("The copy is damaged (quick_check failed).");
            int version = (int)(Db.Scalar<long?>(c, "SELECT max(version) FROM schema_version") ?? 0);
            if (version > Migrator.All.Count) throw new InvalidDataException($"The copy comes from a newer BuzzDesk (schema {version}); update BuzzDesk first.");
            int Count(string sql) { try { return (int)Db.Scalar<long>(c, sql); } catch (SqliteException) { return 0; } }
            DateTime? created = DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file).Replace(Prefix, "").Replace("buzzdesk-", ""), ["yyyyMMdd-HHmmss", "yyyyMMdd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var w) ? w : null;
            return new Inspection(temp, version, created,
                Count("SELECT count(*) FROM task WHERE deleted_utc IS NULL AND parent_task_id IS NULL"), Count("SELECT count(*) FROM topic WHERE deleted_utc IS NULL"),
                Count("SELECT count(*) FROM link WHERE deleted_utc IS NULL"), Count("SELECT count(*) FROM habit WHERE archived_utc IS NULL"),
                Count("SELECT count(*) FROM bill WHERE archived_utc IS NULL"));
        }
        catch { SqliteConnection.ClearAllPools(); File.Delete(temp); throw; }
    }

    /// <summary>Replaces the live database with an inspected copy: a safety backup of the current data first, then
    /// SQLite's backup API copies the pages in (safe with WAL), then pending migrations run.</summary>
    public static void Apply(Inspection copy, string safetyDir)
    {
        using (var live = Db.Open())
        {
            Backup.Run(live, safetyDir, "pre-restore");
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy.TempDb, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            source.Open();
            source.BackupDatabase(live);
        }
        SqliteConnection.ClearAllPools();
        try { File.Delete(copy.TempDb); } catch (IOException) { }
        Db.Initialize();
        Db.RaiseChanged("all");
    }
}
