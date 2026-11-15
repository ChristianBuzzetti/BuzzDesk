using Microsoft.Data.Sqlite;

namespace BuzzDesk.Data;

/// <summary>Runs work in one connection and one transaction (commit on success), then announces the change.</summary>
public static class Repo
{
    public static T Write<T>(string what, Func<SqliteConnection, SqliteTransaction, T> work)
    {
        T result;
        using (var c = Db.Open())
        using (var tx = Db.Begin(c))
        {
            result = work(c, tx);
            tx.Commit();
        }
        Db.RaiseChanged(what);
        return result;
    }

    public static void Write(string what, Action<SqliteConnection, SqliteTransaction> work) => Write(what, (c, tx) => { work(c, tx); return 0; });

    public static T Read<T>(Func<SqliteConnection, T> work)
    {
        using var c = Db.Open();
        return work(c);
    }
}
