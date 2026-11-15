using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using BuzzDesk.Data;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace BuzzDesk;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;
    public AppearanceSettings Appearance { get; private set; } = new();
    public event Action<AppearanceSettings>? AppearanceChanged;
    public bool Exiting { get; private set; }
    public static bool IsShot { get; private set; }
    public static string? ShotDir { get; private set; }

    MainWindow? main;
    public MainWindow? Desk => main;
    WinForms.NotifyIcon? tray;
    HotkeyWindow? hotkeys;
    Mutex? mutex;
    Action? balloonClick;

    static string? Arg(string[] args, string name) { int i = Array.IndexOf(args, name); return i < 0 ? null : i + 1 < args.Length ? args[i + 1] : ""; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? iconDir = Arg(e.Args, "--icon");
        string? shotDir = Arg(e.Args, "--shot");
        IsShot = shotDir != null;
        if (IsShot) ShotDir = shotDir!.Length > 0 ? shotDir : Path.GetTempPath();

        if (!IsShot && iconDir == null)
        {
            mutex = new Mutex(false, AppInfo.Id + "_single_instance");
            bool owned;
            // A language switch relaunches the app: the new process waits for the old one to let go.
            try { owned = mutex.WaitOne(Array.IndexOf(e.Args, "--relaunch") >= 0 ? 8000 : 0); } catch (AbandonedMutexException) { owned = true; }
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.Id + "_show");
            if (!owned) { showSignal.Set(); Shutdown(); return; }
            ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, -1, false);
        }

        Store.Init();
        L.Lang = Arg(e.Args, "--lang") is { Length: > 0 } forced ? forced : Store.Settings.Language == "es" ? "es" : "en";
        var culture = L.Culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        BuzzDesk.Domain.QuickParse.MonthFirst = !L.Es;
        BobertoMark.Current = MarkStyle.FromSettings(Store.Settings);
        LoadAppearance();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => Themes.ApplyChrome((Window)sender, Themes.Current)));
        if (iconDir != null) { _ = IconMaker.RunAsync(iconDir.Length > 0 ? iconDir : Path.GetTempPath()); return; }

        // Windows changed its time zone: "today" follows it when the app uses Windows' zone.
        SystemEvents.TimeChanged += (_, _) => Dispatcher.BeginInvoke(Clock.SystemZoneChanged);

        if (!OpenDatabase()) { Shutdown(); return; }
        StartDayWatch();

        if (Appearance.BobertoVisible) BobertoRuntime.Prewarm(BobertoRuntime.DefaultProfile);

        main = new MainWindow();
        if (IsShot) return;

        SetupTray();
        hotkeys = new HotkeyWindow();
        RegisterHotkeys();
        if (!LaunchedAtStartup(e.Args)) main.Show();
        // WebView2 (Boberto, Studio) ships with Windows 11; on a PC without it, offer Microsoft's installer once.
        if (!Dependencies.WebView2Installed() && !Store.Settings.WebView2Declined)
            Dispatcher.BeginInvoke(async () => await OfferWebView2Async(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (tray != null) tray.Visible = false;
        hotkeys?.Dispose();
        try { mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        base.OnExit(e);
    }

    // ------------------------------------------------------------------ database
    /// <summary>Opens (creating or migrating) %LOCALAPPDATA%\BuzzDesk\buzzdesk.db. --shot uses a throwaway demo
    /// database instead, never the person's data.</summary>
    bool OpenDatabase()
    {
        string path = DataFolder.File("buzzdesk.db");
        string backups = Store.Settings.BackupFolder.Length > 0 ? Store.Settings.BackupFolder : DataFolder.File("backups");
        if (IsShot) { path = Path.Combine(ShotDir!, "demo", "buzzdesk.db"); backups = Path.Combine(ShotDir!, "demo", "backups"); if (File.Exists(path)) File.Delete(path); }
        Db.Configure(path, backups);
        try
        {
            Db.Initialize(c => Sections.SeedDefaults(c, L.T("Trabajo"), L.T("Personal")));
            return true;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            MessageBox.Show(L.T("No se pudo abrir la base de datos de BuzzDesk.") + "\n\n" + Db.Path + "\n\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Raised when the day changes in the app's time zone (and when the zone changes).</summary>
    public event Action? DayChanged;
    DateOnly watchedDay;
    System.Windows.Threading.DispatcherTimer? dayTimer;

    /// <summary>Once a minute: a new day refreshes the views and takes the daily backup.</summary>
    void StartDayWatch()
    {
        watchedDay = Clock.Today;
        Clock.Changed += CheckDay;
        dayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        dayTimer.Tick += (_, _) => CheckDay();
        dayTimer.Start();
        if (!IsShot) Dispatcher.BeginInvoke(DailyBackup, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    void CheckDay()
    {
        if (Clock.Today == watchedDay) return;
        watchedDay = Clock.Today;
        DayChanged?.Invoke();
        if (!IsShot) DailyBackup();
    }

    /// <summary>Today's backup in the background (SQLite's online backup; the app keeps working).</summary>
    void DailyBackup()
    {
        var st = Store.Settings;
        var now = Clock.Now;
        Task.Run(() =>
        {
            try { Backup.DailyIfDue(Db.BackupDir, st.BackupKeepDaily, st.BackupKeepWeekly, now); }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Dispatcher.BeginInvoke(() => Toast(L.T("Copia de seguridad"), L.T("No se pudo hacer la copia de seguridad diaria: ") + ex.Message));
            }
        });
    }

    // ------------------------------------------------------------------ components
    /// <summary>Installs WebView2 if the person agrees, then restarts so Boberto loads; declining is remembered.</summary>
    public async Task OfferWebView2Async()
    {
        if (await Dependencies.OfferWebView2Async(main))
        {
            if (PromptWindow.Confirm(main, L.T("WebView2 instalado"), L.T("Reiniciá BuzzDesk para que aparezca Boberto."), L.T("Reiniciar"), L.T("Más tarde"))) Relaunch();
        }
        else { Store.Settings.WebView2Declined = true; Store.SaveSettings(); }
    }

    /// <summary>Help > Check components: offers what is missing, or says all is in place.</summary>
    public async Task CheckComponentsAsync()
    {
        if (Dependencies.WebView2Installed()) { PromptWindow.Info(main, L.T("Componentes"), "WebView2: " + L.T("Instalado · muestra a Boberto")); return; }
        Store.Settings.WebView2Declined = false;
        await OfferWebView2Async();
    }

    void Relaunch()
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--relaunch") { UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return; }
        ExitApp();
    }

    // ------------------------------------------------------------------ language
    /// <summary>Saves the language and restarts BuzzDesk so every window is rebuilt in it.</summary>
    public void SetLanguage(string lang)
    {
        if (lang == L.Lang) return;
        Store.Settings.Language = lang;
        Store.SaveSettings();
        Relaunch();
    }

    // ------------------------------------------------------------------ appearance (DBMON's AppearanceSettings)
    void LoadAppearance()
    {
        try { Appearance = AppearanceStore.Load(AppearanceStore.PathName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { Appearance = new(); }
        ApplyAppearance(Appearance);
    }

    public void ApplyAppearance(AppearanceSettings settings)
    {
        settings.Validate();
        Appearance = settings;
        var palette = Themes.WithAccent(Themes.All.Single(p => p.Id == settings.ThemeId), settings.AccentHex);
        Themes.Apply(palette);
        AppTypography.Apply(settings);
        foreach (Window w in Windows) Themes.ApplyChrome(w, palette);
        ApplyMarkIcon();
        AppearanceChanged?.Invoke(settings);
    }

    public void SaveAppearance()
    {
        if (IsShot) return;
        try { AppearanceStore.Save(AppearanceStore.PathName, Appearance); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { MessageBox.Show(L.T("No se pudo guardar la apariencia: ") + ex.Message, AppInfo.Name); }
    }

    bool personalizing;
    public void OpenPersonalization(Window owner, bool studio = false)
    {
        if (personalizing) return;
        personalizing = true;
        try { new PersonalizationWindow(Appearance, ApplyAppearance, studio) { Owner = owner }.ShowDialog(); }
        finally { personalizing = false; }
        main?.EnsureBobertoLive();
        SaveAppearance();
    }

    // ------------------------------------------------------------------ logo
    /// <summary>Restyles the BuzzDesk logo everywhere (header marks, window and tray icons) and saves it.</summary>
    System.Windows.Threading.DispatcherTimer? markCommit;
    public void SetMark(MarkStyle style)
    {
        BobertoMark.Current = style;
        style.Save(Store.Settings);
        markCommit ??= new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(450) };
        markCommit.Tick -= MarkCommitTick; markCommit.Tick += MarkCommitTick;
        markCommit.Stop(); markCommit.Start();
    }
    void MarkCommitTick(object? sender, EventArgs e) { markCommit!.Stop(); Store.SaveSettings(); ApplyMarkIcon(); }

    /// <summary>Window and tray icons follow the custom logo (the .exe file keeps the shipped icon).</summary>
    void ApplyMarkIcon()
    {
        var accent = Resources["Accent"] as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Crimson;
        Resources["AppIcon"] = BobertoMark.Render(64, accent);
        if (tray == null) return;
        var frame = BobertoMark.Render(32, accent);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var bmp = new System.Drawing.Bitmap(ms);
        IntPtr handle = bmp.GetHicon();
        var old = trayIconHandle;
        tray.Icon = System.Drawing.Icon.FromHandle(handle);
        trayIconHandle = handle;
        if (old != IntPtr.Zero) DestroyIcon(old);
    }
    IntPtr trayIconHandle;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

    // ------------------------------------------------------------------ tray
    void SetupTray()
    {
        tray = new WinForms.NotifyIcon { Text = AppInfo.Name, Visible = true };
        ApplyMarkIcon();
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ShowMain();
            else if (e.Button == WinForms.MouseButtons.Right) ShowTrayMenu();
        };
        tray.BalloonTipClicked += (_, _) => balloonClick?.Invoke();
    }

    void ShowTrayMenu()
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        void Item(string header, string gesture, Action click)
        {
            var m = new MenuItem { Header = header, InputGestureText = gesture };
            m.Click += (_, _) => click();
            menu.Items.Add(m);
        }
        Item(L.T("Captura rápida"), Store.Settings.HotkeyCapture, QuickCapture);
        menu.Items.Add(new Separator());
        Item(L.T("Hoy"), "", () => Navigate("today"));
        Item(L.T("Pendientes"), "", () => Navigate("tasks"));
        Item(L.T("Temas"), "", () => Navigate("topics"));
        Item(L.T("Enlaces"), "", () => Navigate("links"));
        menu.Items.Add(new Separator());
        Item(L.T("Personalizar…"), "", () => { ShowMain(); OpenPersonalization(main!); });
        menu.Items.Add(new Separator());
        Item(L.T("Salir"), "", ExitApp);
        // A context menu needs a foreground window of this process to close when clicking elsewhere.
        var anchor = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, Width = 0, Height = 0, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, Left = -100, Top = -100 };
        anchor.Show(); anchor.Activate();
        menu.Closed += (_, _) => anchor.Close();
        menu.IsOpen = true;
    }

    /// <summary>Tray balloon (simple notices; reminders use Windows toasts).</summary>
    public void Toast(string title, string text, Action? click = null)
    {
        balloonClick = click;
        tray?.ShowBalloonTip(2500, title, string.IsNullOrEmpty(text) ? " " : text, WinForms.ToolTipIcon.None);
    }

    public void ShowMain()
    {
        if (main == null) return;
        if (!main.IsVisible) main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
    }

    public void Navigate(string view) { ShowMain(); main?.Navigate(view); }

    /// <summary>Quick capture (Ctrl+Alt+Space): until the capture window exists, opens BuzzDesk.</summary>
    public void QuickCapture() => ShowMain();

    public void ExitApp()
    {
        if (Exiting) return;
        Exiting = true;
        foreach (Window w in Windows.Cast<Window>().ToArray()) w.Close();
        Shutdown();
    }

    // ------------------------------------------------------------------ hotkeys
    public string CaptureHotkeyText { get; private set; } = "";
    const int HotkeyQuickCapture = 1;

    void RegisterHotkeys()
    {
        var st = Store.Settings;
        if (hotkeys!.Register(HotkeyQuickCapture, st.HotkeyCapture)) CaptureHotkeyText = st.HotkeyCapture;
        else { CaptureHotkeyText = ""; Toast(L.T("Atajos de teclado"), L.F("No se pudo registrar {0}.", st.HotkeyCapture)); }
        main?.RefreshShortcuts();
    }

    internal void OnHotkey(int id)
    {
        if (id == HotkeyQuickCapture) QuickCapture();
    }

    // ------------------------------------------------------------------ startup with Windows
#if STORE
    // Packaged (MSIX): a write to HKCU\...\Run is virtualized and does nothing, so the package declares a startup task
    // (AppxManifest: TaskId "BuzzDeskStartup") and the app turns it on or off.
    const string StartupTaskId = "BuzzDeskStartup";

    static global::Windows.ApplicationModel.StartupTaskState StartupState(bool? enable)
    {
        return Task.Run(async () =>
        {
            var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            if (enable == true && task.State == global::Windows.ApplicationModel.StartupTaskState.Disabled) return await task.RequestEnableAsync();
            if (enable == false && task.State == global::Windows.ApplicationModel.StartupTaskState.Enabled) task.Disable();
            return task.State;
        }).GetAwaiter().GetResult();
    }

    public static bool StartupEnabled
    {
        get
        {
            try { return StartupState(null) is global::Windows.ApplicationModel.StartupTaskState.Enabled or global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy; }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException) { return false; }
        }
        set
        {
            global::Windows.ApplicationModel.StartupTaskState state;
            try { state = StartupState(value); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException) { return; }
            if (value && state == global::Windows.ApplicationModel.StartupTaskState.DisabledByUser)
                PromptWindow.Info(Current.main, L.T("Iniciar con Windows"), L.T("Lo desactivaste desde Windows. Para volver a activarlo, abrí Configuración → Aplicaciones → Inicio y encendé BuzzDesk."));
        }
    }

    static bool LaunchedAtStartup(string[] args)
    {
        if (Array.IndexOf(args, "--tray") >= 0) return true;
        try { return global::Windows.ApplicationModel.AppInstance.GetActivatedEventArgs()?.Kind == global::Windows.ApplicationModel.Activation.ActivationKind.StartupTask; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return false; }
    }
#else
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool StartupEnabled
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(AppInfo.Id) != null; }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(AppInfo.Id, "\"" + Environment.ProcessPath + "\" --tray");
            else k.DeleteValue(AppInfo.Id, false);
        }
    }

    static bool LaunchedAtStartup(string[] args) => Array.IndexOf(args, "--tray") >= 0;
#endif
}
