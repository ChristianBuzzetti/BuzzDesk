using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>
/// The SQLite database (%LOCALAPPDATA%\BuzzDesk\buzzdesk.db). Every connection gets the per-connection PRAGMAs
/// (foreign keys, busy timeout, synchronous); WAL and application_id are persistent and set once by
/// <see cref="Initialize"/>, which also backs up and migrates. Connections are pooled by Microsoft.Data.Sqlite, so
/// opening one per operation is cheap and keeps each operation self-contained.
/// </summary>
public static class Db
{
    /// <summary>PRAGMA application_id: 'BZDK' — identifies the file as a BuzzDesk database.</summary>
    public const int ApplicationId = 0x425A444B;

    public static string Path { get; private set; } = "";
    public static string BackupDir { get; private set; } = "";
    static string connectionString = "";

    /// <summary>Raised after any write, with the kind of data that changed ("task", "topic", …).</summary>
    public static event Action<string>? Changed;
    public static void RaiseChanged(string what) => Changed?.Invoke(what);

    /// <summary>Points at the database file and its backup folder; does not open anything.</summary>
    public static void Configure(string path, string backupDir)
    {
        Path = System.IO.Path.GetFullPath(path);
        BackupDir = System.IO.Path.GetFullPath(backupDir);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            ForeignKeys = true,           // also sent as PRAGMA below, so it holds even if the builder default changes
            DefaultTimeout = 30,
        }.ToString();
    }

    public static SqliteConnection Open()
    {
        if (connectionString.Length == 0) throw new InvalidOperationException("Db.Configure was not called.");
        var c = new SqliteConnection(connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY;";
        cmd.ExecuteNonQuery();
        return c;
    }

    /// <summary>Creates or opens the database: checks it is ours, turns on WAL, backs up before pending migrations,
    /// migrates and seeds defaults. Returns the schema version.</summary>
    public static int Initialize(Action<SqliteConnection>? seed = null)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        bool existed = File.Exists(Path) && new FileInfo(Path).Length > 0;
        using (var c = Open())
        {
            long appId = Scalar<long>(c, "PRAGMA application_id;");
            if (existed && appId != 0 && appId != ApplicationId)
                throw new InvalidDataException("The file is not a BuzzDesk database: " + Path);
            if (Scalar<string>(c, "PRAGMA journal_mode = WAL;") is not "wal")
                throw new InvalidOperationException("Could not turn on WAL for " + Path);
            if (appId != ApplicationId) Exec(c, $"PRAGMA application_id = {ApplicationId};");
            if (existed && Migrator.Pending(c).Count > 0) Backup.Run(c, BackupDir, "pre-migration");
            Migrator.Migrate(c);
            seed?.Invoke(c);
            Exec(c, "PRAGMA optimize;");
            return Migrator.CurrentVersion(c);
        }
    }

    // ------------------------------------------------------------------ helpers
    // Microsoft.Data.Sqlite wants every command inside a transaction to name it; the helpers below attach the
    // connection's open transaction (begun with Begin) so callers don't have to.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SqliteConnection, SqliteTransaction> ambient = new();

    /// <summary>Begins a transaction that the helpers (Exec, Query, Scalar, Insert) use automatically.</summary>
    public static SqliteTransaction Begin(SqliteConnection c)
    {
        var tx = c.BeginTransaction();
        ambient.AddOrUpdate(c, tx);
        return tx;
    }

    public static SqliteCommand Cmd(SqliteConnection c, string sql, params (string name, object? value)[] args)
    {
        var cmd = c.CreateCommand();
        if (ambient.TryGetValue(c, out var tx) && tx.Connection == c) cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static int Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, sql, args);
        return cmd.ExecuteNonQuery();
    }

    public static T? Scalar<T>(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, sql, args);
        object? v = cmd.ExecuteScalar();
        if (v == null || v is DBNull) return default;
        return (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
    }

    public static long Insert(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, sql + "; SELECT last_insert_rowid();", args);
        return (long)cmd.ExecuteScalar()!;
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    // ------------------------------------------------------------------ value formats (see 0001_initial.sql)
    public const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";

    public static string Utc(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString(UtcFormat, CultureInfo.InvariantCulture);
    public static string NowUtc() => Utc(DateTime.UtcNow);
    public static DateTime ParseUtc(string s) => DateTime.ParseExact(s, UtcFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    public static string Date(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);
    public static DateOnly ParseDate(string s) => DateOnly.ParseExact(s, DateFormat, CultureInfo.InvariantCulture);
    public static string Time(TimeOnly t) => t.ToString(TimeFormat, CultureInfo.InvariantCulture);
    public static TimeOnly ParseTime(string s) => TimeOnly.ParseExact(s, TimeFormat, CultureInfo.InvariantCulture);

    public static string? Str(SqliteDataReader r, string col) { int i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetString(i); }
    public static long? Long(SqliteDataReader r, string col) { int i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetInt64(i); }
    public static DateOnly? DateOrNull(SqliteDataReader r, string col) => Str(r, col) is string s ? ParseDate(s) : null;
    public static TimeOnly? TimeOrNull(SqliteDataReader r, string col) => Str(r, col) is string s ? ParseTime(s) : null;
    public static DateTime? UtcOrNull(SqliteDataReader r, string col) => Str(r, col) is string s ? ParseUtc(s) : null;
    public static object? Value(DateOnly? d) => d is DateOnly x ? Date(x) : null;
    public static object? Value(TimeOnly? t) => t is TimeOnly x ? Time(x) : null;
    public static object? Value(DateTime? utc) => utc is DateTime x ? Utc(x) : null;

    // ------------------------------------------------------------------ app_meta
    public static string? GetMeta(SqliteConnection c, string key) => Scalar<string>(c, "SELECT value FROM app_meta WHERE key = $k", ("$k", key));
    public static void SetMeta(SqliteConnection c, string key, string value) =>
        Exec(c, "INSERT INTO app_meta(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));
}
