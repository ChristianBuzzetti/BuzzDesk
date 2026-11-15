using System.IO;
using System.Text;

namespace BuzzDesk;

/// <summary>
/// A small daily log in %LOCALAPPDATA%\BuzzDesk\logs\buzzdesk-YYYYMMDD.log (14 days kept): startup, database
/// version, backups, cloud copies, notification problems and any error. No personal data is written (no task
/// titles, no URLs), so it can be sent along when something goes wrong.
/// </summary>
public static class Log
{
    static readonly object gate = new();
    public static string Dir => DataFolder.File("logs");
    public static string Today => Path.Combine(Dir, "buzzdesk-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string where, Exception ex) => Write("ERROR", where + ": " + ex);

    static void Write(string level, string message)
    {
        if (App.IsShot) return;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Today, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + level + "  " + message.Replace("\r\n", "\n").Replace("\n", "\n                     ") + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Deletes logs older than 14 days.</summary>
    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var f in Directory.GetFiles(Dir, "buzzdesk-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Version, Windows, .NET, data folder, schema: what support needs first.</summary>
    public static string Environment_()
    {
        var v = typeof(App).Assembly.GetName().Version;
        return "BuzzDesk " + v + " · Windows " + Environment.OSVersion.Version + " · .NET " + Environment.Version +
               " · " + (Environment.Is64BitProcess ? "x64" : "x86") + " · lang " + L.Lang + " · tz " + Clock.Zone.Id;
    }
}
