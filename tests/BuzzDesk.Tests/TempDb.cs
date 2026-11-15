using BuzzDesk.Data;

namespace BuzzDesk.Tests;

/// <summary>A fresh database in a temp folder for one test class; deleted afterwards.</summary>
public sealed class TempDb : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "buzzdesk-tests-" + Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(Dir, "buzzdesk.db");
    public string BackupDir => Path.Combine(Dir, "backups");

    public TempDb()
    {
        Directory.CreateDirectory(Dir);
        Db.Configure(DbPath, BackupDir);
        Db.Initialize(c => Sections.SeedDefaults(c, "Work", "Personal"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}

/// <summary>Tests that touch Db run one at a time (Db is a static, process-wide configuration).</summary>
[CollectionDefinition("db", DisableParallelization = true)]
public sealed class DbCollection { }
