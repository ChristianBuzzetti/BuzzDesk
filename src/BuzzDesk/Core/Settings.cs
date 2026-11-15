#nullable disable
using System.IO;
using System.Text.Json;

namespace BuzzDesk
{
    /// <summary>App settings (JSON in LocalAppData\BuzzDesk\settings.json). Appearance lives apart (AppearanceStore),
    /// the person's data lives in SQLite (Data.Db).</summary>
    public class Settings
    {
        public string HotkeyCapture = "Ctrl+Alt+Space";   // quick capture, anywhere in Windows
        public string Language = "en";                    // en | es
        /// <summary>Windows time zone id used for "today", due dates and reminders. Empty = follow Windows.</summary>
        public string TimeZoneId = "";
        public bool GuideOffered = false;                 // the guide is offered once, on the first start
        public bool WebView2Declined = false;             // asked once at startup; Help > Check components asks again
        public bool TrayHintShown = false;                // "BuzzDesk keeps running in the tray" shown once
        public bool StartupOffered = false;               // asked once, with the first reminder, to start with Windows
        public int BackupKeepDaily = 14;                  // daily copies kept in backups\
        public int BackupKeepWeekly = 8;                  // plus one per week (Mondays) for this many weeks
        public string BackupFolder = "";                  // empty = LocalAppData\BuzzDesk\backups
        public string LastNav = "today";                  // today | tasks | topics | links
        public string QuickMode = "task";                 // quick capture: task | note | reminder | link
        public long QuickSectionId = 0;                   // quick capture: last area or project for tasks (0 = none)
        public bool FetchFavicons = true;                 // ask each saved link's own site for its title and icon
        public string HotkeyLauncher = "Ctrl+Alt+L";      // quick link launcher, anywhere in Windows
        public string MarkShape = "hex";                  // BuzzDesk logo (Personalize → Effects → Logo)
        public string MarkHead = "#0E1015";
        public string MarkEyes = "oval";
        public string MarkTile = "accent";
        // AI: off until the person turns it on; the API key is stored apart, encrypted (AiKeys).
        public bool AiEnabled = false;
        public string AiProvider = "anthropic";           // id from AiProviders.All
        public string AiModel = "";                       // empty = the provider's suggested model
        public string AiBaseUrl = "";                     // only for "custom" (any OpenAI-compatible endpoint)
        public bool AiAutoCaption = false;                // unused in BuzzDesk v1 (kept for the shared AI settings window)
        public List<AiConnection> AiConnections = new List<AiConnection>();
        public string AiActiveId = "";
    }

    /// <summary>One saved AI connection: provider, model and (for custom services) the endpoint. Its API key is kept
    /// encrypted under its Id, never in this JSON.</summary>
    public class AiConnection
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string Provider = "anthropic";
        public string Model = "";
        public string BaseUrl = "";
    }

    public static class Store
    {
        public static Settings Settings = new Settings();
        static readonly MiniJson Json = new MiniJson();

        public static void Init() { LoadSettings(); }

        static string SettingsPath { get { Directory.CreateDirectory(DataFolder.Root); return DataFolder.File("settings.json"); } }

        public static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    Settings = Json.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Settings = new Settings(); }
            SaveSettings();
        }

        /// <summary>True during --shot: nothing the screenshots change reaches settings.json.</summary>
        public static bool ReadOnly;

        public static void SaveSettings()
        {
            if (ReadOnly) return;
            try { AtomicFiles.SaveUtf8(SettingsPath, Json.Serialize(Settings)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>System.Text.Json with public fields (the settings shape shared with BuzzPrint).</summary>
    public sealed class MiniJson
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        public string Serialize(object value) { return JsonSerializer.Serialize(value, value.GetType(), Options); }
        public T Deserialize<T>(string text) { return JsonSerializer.Deserialize<T>(text, Options); }
    }
}
