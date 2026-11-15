using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>Cloud folders on this PC, the encryption passphrase (DPAPI) and the backups to the chosen folder.</summary>
public static class Cloud
{
    public sealed record Place(string Service, string Root);

    /// <summary>Folders that a cloud client syncs, as found on this PC (OneDrive personal and work, Google Drive,
    /// Dropbox, iCloud).</summary>
    public static List<Place> Detect()
    {
        var list = new List<Place>();
        void Add(string service, string? path) { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && list.All(p => !p.Root.Equals(path, StringComparison.OrdinalIgnoreCase))) list.Add(new(service, path)); }
        Add("OneDrive", Environment.GetEnvironmentVariable("OneDriveConsumer"));
        Add("OneDrive", Environment.GetEnvironmentVariable("OneDrive"));
        Add("OneDrive (" + L.T("trabajo") + ")", Environment.GetEnvironmentVariable("OneDriveCommercial"));
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Google Drive for desktop mounts a drive (G: by default) with "My Drive" / "Mi unidad"; older clients used a folder.
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                foreach (var name in new[] { "My Drive", "Mi unidad", "Meine Ablage", "Mon Drive", "Il mio Drive", "Meu Drive" })
                    Add("Google Drive", Path.Combine(d.RootDirectory.FullName, name));
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        Add("Google Drive", Path.Combine(home, "Google Drive"));
        Add("Google Drive", Path.Combine(home, "My Drive"));
        foreach (var info in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
        {
            string f = Path.Combine(info, "Dropbox", "info.json");
            try
            {
                if (!File.Exists(f)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                foreach (var account in doc.RootElement.EnumerateObject())
                    if (account.Value.TryGetProperty("path", out var path)) Add("Dropbox" + (account.Name == "business" ? " (" + L.T("trabajo") + ")" : ""), path.GetString());
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        Add("Dropbox", Path.Combine(home, "Dropbox"));
        Add("iCloud Drive", Path.Combine(home, "iCloudDrive"));
        return list;
    }

    /// <summary>The subfolder BuzzDesk uses inside a cloud folder.</summary>
    public static string Target(string root) => Path.Combine(root, "BuzzDesk Backups");

    public static string? ServiceOf(string folder) => Detect().FirstOrDefault(p => folder.StartsWith(p.Root, StringComparison.OrdinalIgnoreCase))?.Service;

    // ------------------------------------------------------------------ passphrase (DPAPI, this Windows user only)
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BuzzDesk.CloudKey.v1");
    static string KeyPath => DataFolder.File("cloud.key");

    public static void SavePassphrase(string passphrase)
    {
        Directory.CreateDirectory(DataFolder.Root);
        File.WriteAllBytes(KeyPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(passphrase), Entropy, DataProtectionScope.CurrentUser));
    }

    public static string? LoadPassphrase()
    {
        try { return File.Exists(KeyPath) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), Entropy, DataProtectionScope.CurrentUser)) : null; }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException) { return null; }
    }

    public static bool HasPassphrase => LoadPassphrase() != null;

    // ------------------------------------------------------------------ backups
    public static bool Enabled => Store.Settings.CloudFolder.Length > 0;

    /// <summary>Set by any write: the next exit (or day change) uploads a copy.</summary>
    public static bool Dirty { get; set; }

    /// <summary>One copy to the chosen folder; records the result in the settings. Throws nothing.</summary>
    public static (bool ok, string message) Upload()
    {
        var st = Store.Settings;
        if (!Enabled) return (false, L.T("La copia en la nube está apagada."));
        string? pass = st.CloudEncrypt ? LoadPassphrase() : null;
        if (st.CloudEncrypt && pass == null) return Fail(L.T("Falta la frase de cifrado: configurala en Archivo → Copia en la nube."));
        try
        {
            CloudBackup.Result r;
            using (var c = Db.Open()) r = CloudBackup.Run(c, st.CloudFolder, pass, Clock.Now, st.CloudKeepDaily, st.CloudKeepMonthly);
            st.CloudLastUtc = Db.NowUtc();
            st.CloudLastError = "";
            Store.SaveSettings();
            Dirty = false;
            return (true, L.F("Copia subida: {0} ({1:N0} KB)", Path.GetFileName(r.Path), r.Bytes / 1024));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or InvalidDataException or CryptographicException)
        {
            return Fail(ex.Message);
        }

        (bool, string) Fail(string message)
        {
            st.CloudLastError = message;
            Store.SaveSettings();
            return (false, message);
        }
    }

    /// <summary>A copy today already? (the daily upload runs once a day).</summary>
    public static bool DoneToday => Store.Settings.CloudLastUtc.Length > 0 && DateOnly.FromDateTime(Clock.ToLocal(Db.ParseUtc(Store.Settings.CloudLastUtc))) == Clock.Today;

    /// <summary>"OneDrive · hace 2 h" for the footer.</summary>
    public static string Status()
    {
        var st = Store.Settings;
        if (!Enabled) return L.T("apagada");
        string where = ServiceOf(st.CloudFolder) ?? Path.GetFileName(Path.GetDirectoryName(st.CloudFolder.TrimEnd('\\')) ?? st.CloudFolder);
        if (st.CloudLastError.Length > 0) return where + " · " + L.T("error");
        return where + " · " + (st.CloudLastUtc.Length > 0 ? Fmt.Ago(Db.ParseUtc(st.CloudLastUtc)) : L.T("pendiente"));
    }
}
