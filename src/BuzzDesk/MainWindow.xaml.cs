using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BuzzDesk;

public partial class MainWindow : Window
{
    BobertoDockControl? bobertoDock;
    bool companionAllowed, shellMotion;
    AppearanceSettings appearance => App.Current.Appearance;
    readonly Dictionary<string, DeskView> views = [];
    DeskView? current;
    public DeskView? CurrentView => current;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.Name;
        LangEnMenu.IsChecked = !L.Es;
        LangEsMenu.IsChecked = L.Es;
        StartupMenu.IsChecked = App.StartupEnabled;

        App.Current.AppearanceChanged += OnAppearance;
        ContentRendered += (_, _) => { if (companionAllowed) return; companionAllowed = true; SyncBobertoDock(appearance); };
        StateChanged += (_, _) => _ = bobertoDock?.PauseAsync(WindowState == WindowState.Minimized || !appearance.BobertoVisible);
        SizeChanged += (_, _) => AdaptHeader();
        HeaderLeft.SizeChanged += (_, _) => SizeBobertoBand();
        Closing += (_, e) => { if (!App.Current.Exiting && !App.IsShot) { e.Cancel = true; HideToTray(); } };
        Closed += (_, _) =>
        {
            foreach (var v in views.Values) v.Release();
            if (bobertoDock != null) { BobertoHost.Content = null; bobertoDock.Dispose(); bobertoDock = null; }
        };
        PreviewKeyDown += OnKey;
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(OfferGuide, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Clock.Changed += () => { ShowZone(); current?.Refresh(); };

        OnAppearance(appearance);
        RefreshShortcuts();
        ShowZone();
        SyncAiChip();
        Navigate(Store.Settings.LastNav);
    }

    // ================================================================== navigation
    DeskView CreateView(string key) => key switch
    {
        "tasks" => new PlaceholderView("tasks", L.T("Pendientes")),
        "topics" => new PlaceholderView("topics", L.T("Temas")),
        "links" => new PlaceholderView("links", L.T("Enlaces")),
        _ => new TodayView(),
    };

    public void Navigate(string key)
    {
        if (key is not ("today" or "tasks" or "topics" or "links" or "search")) key = "today";
        if (!views.TryGetValue(key, out var view)) { view = CreateView(key); views[key] = view; }
        current = view;
        ViewHost.Content = view;
        ViewToolbar.Children.Clear();
        view.BuildToolbar(ViewToolbar);
        view.Shown();
        var nav = key switch { "tasks" => NavTasks, "topics" => NavTopics, "links" => NavLinks, "search" => NavSearch, _ => NavToday };
        if (nav.IsChecked != true) nav.IsChecked = true;
        if (key != "search" && Store.Settings.LastNav != key) { Store.Settings.LastNav = key; Store.SaveSettings(); }
    }

    void NavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key } && current?.Key != key) Navigate(key);
    }

    void GoTodayClick(object sender, RoutedEventArgs e) => Navigate("today");
    void GoTasksClick(object sender, RoutedEventArgs e) => Navigate("tasks");
    void GoTopicsClick(object sender, RoutedEventArgs e) => Navigate("topics");
    void GoLinksClick(object sender, RoutedEventArgs e) => Navigate("links");

    /// <summary>Reloads the visible view (data changed elsewhere: quick capture, a reminder, another view).</summary>
    public void RefreshAll() { current?.Refresh(); }

    public void SetStatus(string text) => StatusText.Text = text;

    void HideToTray()
    {
        Hide();
        if (Store.Settings.TrayHintShown) return;
        Store.Settings.TrayHintShown = true; Store.SaveSettings();
        App.Current.Toast(L.T("BuzzDesk sigue activo"), L.T("Queda en la bandeja para avisarte de tus recordatorios. Salir: clic derecho en el ícono → Salir."));
    }

    // ================================================================== appearance (as DBMON's MainWindow.Appearance)
    void OnAppearance(AppearanceSettings settings)
    {
        ShowBobertoMenu.IsChecked = settings.BobertoVisible;
        BobertoPanel.Visibility = settings.BobertoVisible ? Visibility.Visible : Visibility.Collapsed;
        AdaptHeader();
        SyncBobertoDock(settings);
        ApplyShellMotion(settings);
    }

    void SyncBobertoDock(AppearanceSettings settings)
    {
        bool pause = !settings.BobertoVisible || WindowState == WindowState.Minimized;
        if (bobertoDock != null) { _ = bobertoDock.ApplyThemeAsync(settings); if (bobertoDock.IsPaused != pause) _ = bobertoDock.PauseAsync(pause); return; }
        if (!companionAllowed || !settings.BobertoVisible) return;
        bobertoDock = new BobertoDockControl(settings, null);
        bobertoDock.StudioRequested += () => Dispatcher.BeginInvoke(() => App.Current.OpenPersonalization(this, true));
        BobertoHost.Content = bobertoDock;
        if (pause) _ = bobertoDock.PauseAsync(true);
    }

    public void EnsureBobertoLive() => _ = bobertoDock?.EnsureLiveAsync();

    void AdaptHeader()
    {
        bool shortWindow = ActualHeight is > 0 and < 720;
        Resources["TopMenuGlyph"] = ActualWidth is > 0 and < 1500 ? Visibility.Collapsed : Visibility.Visible;
        bool narrow = ActualWidth is > 0 and < 1180;
        CaptureKeyHint.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.Width = narrow ? 220 : ActualWidth < 1500 ? 280 : 340;
        HeaderBar.Padding = shortWindow ? new Thickness(20, 6, 16, 6) : new Thickness(20, 10, 16, 10);
        SidebarColumn.Width = new GridLength(ActualWidth is > 0 and < 1000 ? 0 : 236);
        SizeBobertoBand();
    }

    const double BobertoMinWindow = 900, BobertoMaxShare = 0.30, BobertoMaxHeight = 200;
    void SizeBobertoBand()
    {
        double width = BobertoBandWidth();
        BobertoColumn.Width = new GridLength(width);
        BobertoPanel.Width = width;
        // The header grows so Boberto's scene keeps its 16:9 shape at that width instead of a thin letterbox.
        double tall = width > 0 ? Math.Min(Math.Round(width * 9 / 16), BobertoMaxHeight) : 0;
        if (Math.Abs(HeaderLeft.MinHeight - tall) > 1) HeaderLeft.MinHeight = tall;
        BobertoPanel.Height = Math.Max(tall, HeaderLeft.ActualHeight);
    }
    double BobertoBandWidth()
    {
        if (!appearance.BobertoVisible || ActualWidth is > 0 and < BobertoMinWindow) return 0;
        double height = HeaderLeft.ActualHeight > 0 ? HeaderLeft.ActualHeight : 150;
        double width = Math.Min(Math.Round(height * 16 / 9), ActualWidth > 0 ? Math.Round(ActualWidth * BobertoMaxShare) : double.MaxValue);
        return Math.Abs(BobertoColumn.Width.Value - width) <= 2 ? BobertoColumn.Width.Value : width;
    }

    // DBMON's living details: the topbar edge sweeps every 7 s and the brand dot pulses; both still when motion is off.
    void ApplyShellMotion(AppearanceSettings settings)
    {
        var sweep = (Color)Application.Current.Resources["EdgeSweepColor"];
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (offset, alpha) in new[] { (0d, 0), (.37, 0), (.5, (int)sweep.A), (.63, 0), (1d, 0) }) brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)alpha, sweep.R, sweep.G, sweep.B), offset));
        var travel = new TranslateTransform(-.65, 0); brush.RelativeTransform = travel;
        bool motion = settings.BobertoMotion && SystemParameters.ClientAreaAnimation;
        EdgeSweep.Fill = motion ? brush : null;
        if (motion) travel.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-.65, .65, TimeSpan.FromSeconds(7)) { RepeatBehavior = RepeatBehavior.Forever });
        BrandDot.BeginAnimation(OpacityProperty, motion ? new DoubleAnimation(1, .45, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        BuildWordmark(motion);
        shellMotion = motion;
    }

    // DBMON's .logo-wave: mono bold letters in dim ink; each one lights up with the accent at 30% of a 3 s cycle, 0.18 s apart.
    public const string WordmarkText = "BUZZDESK";
    void BuildWordmark(bool motion)
    {
        Wordmark.Children.Clear();
        BrandMark.Animated = motion;
        var dim = ((SolidColorBrush)Application.Current.Resources["Muted"]).Color;
        var accent = ((SolidColorBrush)Application.Current.Resources["Accent"]).Color;
        for (int i = 0; i < WordmarkText.Length; i++)
        {
            var ink = new SolidColorBrush(dim);
            var letter = new TextBlock { Text = WordmarkText[i].ToString(), FontWeight = FontWeights.Bold, FontSize = 18, Foreground = ink, Margin = new Thickness(0, 0, 1, 0) };
            letter.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Wordmark.Children.Add(letter);
            if (!motion) continue;
            var wave = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * .18) };
            var ease = new KeySpline(.42, 0, .58, 1);
            wave.KeyFrames.Add(new LinearColorKeyFrame(dim, KeyTime.FromPercent(0)));
            wave.KeyFrames.Add(new SplineColorKeyFrame(accent, KeyTime.FromPercent(.3), ease));
            wave.KeyFrames.Add(new SplineColorKeyFrame(dim, KeyTime.FromPercent(1), ease));
            ink.BeginAnimation(SolidColorBrush.ColorProperty, wave);
        }
    }

    void WordmarkClick(object sender, MouseButtonEventArgs e)
    {
        if (!shellMotion) return;
        var shift = Wordmark.RenderTransform as TranslateTransform ?? new TranslateTransform(); Wordmark.RenderTransform = shift;
        var glitch = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(.5) };
        foreach (var (x, at) in new[] { (0d, 0d), (-2d, .1), (2d, .2), (-1d, .3), (1d, .4), (0d, .5) }) glitch.KeyFrames.Add(new DiscreteDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
        shift.BeginAnimation(TranslateTransform.XProperty, glitch);
    }

    void PersonalizeClick(object sender, RoutedEventArgs e) => App.Current.OpenPersonalization(this);
    void BobertoToggleClick(object sender, RoutedEventArgs e) { App.Current.ApplyAppearance(appearance with { BobertoVisible = ShowBobertoMenu.IsChecked }); App.Current.SaveAppearance(); }

    // ================================================================== shortcuts, zone, AI chip
    public void RefreshShortcuts()
    {
        string key = App.Current.CaptureHotkeyText.Length > 0 ? App.Current.CaptureHotkeyText : Store.Settings.HotkeyCapture;
        StatusCaptureKey.Text = key;
        CaptureKeyHint.Text = key.Replace('+', ' ');
        MenuQuickCapture.InputGestureText = key;
    }

    void ShowZone()
    {
        var z = Clock.Zone;
        var offset = z.GetUtcOffset(DateTime.UtcNow);
        ZoneStatus.Text = "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm") + (Store.Settings.TimeZoneId.Length == 0 ? " · Windows" : "");
    }

    void ZoneMenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender) return;
        ZoneMenu.Items.Clear();
        var follow = new MenuItem { Header = L.T("Usar la de Windows") + "  (" + TimeZoneInfo.Local.DisplayName + ")", IsCheckable = true, IsChecked = Store.Settings.TimeZoneId.Length == 0 };
        follow.Click += (_, _) => Clock.SetZone("");
        ZoneMenu.Items.Add(follow);
        ZoneMenu.Items.Add(new Separator());
        foreach (var z in TimeZoneInfo.GetSystemTimeZones())
        {
            var item = new MenuItem { Header = z.DisplayName, IsCheckable = true, IsChecked = Store.Settings.TimeZoneId == z.Id, Tag = z.Id };
            item.Click += (_, _) => Clock.SetZone((string)item.Tag);
            ZoneMenu.Items.Add(item);
        }
    }

    void ZoneStatusClick(object sender, RoutedEventArgs e)
    {
        var host = (MenuItem)MainMenu.Items[2];
        host.IsSubmenuOpen = true;
        ZoneMenu.IsSubmenuOpen = true;
    }

    static string ProviderShort(AiConnection c) => c.Name.Trim().Length > 0 ? c.Name.Trim() : L.T(Ai.AiProviders.Get(c.Provider).Name);

    public void SyncAiChip()
    {
        var st = Store.Settings;
        var conn = Ai.AiConnections.Active(st);
        bool on = st.AiEnabled && conn != null;
        NavAiText.Text = on ? (L.Es ? "IA · " : "AI · ") + ProviderShort(conn!) : L.T("IA apagada");
        NavAiStatus.ToolTip = on ? Ai.AiConnections.DisplayName(conn!) + " · " + L.T("Configurar IA") : L.T("Configurar IA");
        NavAiDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, on ? "Ok" : "Muted");
    }

    void AiSettingsClick(object sender, RoutedEventArgs e) { AiSettingsWindow.ShowFor(this); SyncAiChip(); }

    // ================================================================== search
    void SearchChanged(object sender, TextChangedEventArgs e) { }
    void FocusSearchClick(object sender, RoutedEventArgs e) { Search.Focus(); Search.SelectAll(); }

    // ================================================================== guide, keys
    void GuideClick(object sender, RoutedEventArgs e) => GuideWindow.Open(this);

    /// <summary>The guide's "try it" buttons.</summary>
    public void GuideAction(string action, Window from)
    {
        switch (action)
        {
            case "quick": App.Current.QuickCapture(); break;
            case "today": case "tasks": case "topics": case "links": Navigate(action); Activate(); break;
            case "search": Activate(); FocusSearchClick(this, new RoutedEventArgs()); break;
            case "personalize": App.Current.OpenPersonalization(this); break;
            case "ai": AiSettingsClick(this, new RoutedEventArgs()); break;
            case "folder": DataFolderClick(this, new RoutedEventArgs()); break;
        }
    }

    /// <summary>Once, on the first start: offer the guide.</summary>
    void OfferGuide()
    {
        if (Store.Settings.GuideOffered || App.IsShot || !IsVisible) return;
        Store.Settings.GuideOffered = true; Store.SaveSettings();
        if (PromptWindow.Confirm(this, L.T("Te damos la bienvenida a BuzzDesk"),
                L.T("¿Querés ver la guía? Explica todo lo que puede hacer la app, paso a paso, con botones para probar cada función.") + "\n\n" +
                L.T("Siempre a mano: Ayuda → Guía de BuzzDesk, o F1"),
                L.T("Ver la guía"), L.T("Más tarde")))
            GuideWindow.Open(this);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.F1) { GuideWindow.Open(this); e.Handled = true; return; }
        if (ctrl && e.Key == Key.F) { FocusSearchClick(this, e); e.Handled = true; return; }
        if (Search.IsKeyboardFocusWithin && e.Key == Key.Escape) { Search.Text = ""; e.Handled = true; return; }
        if (ctrl && e.Key is Key.D1 or Key.NumPad1) { Navigate("today"); e.Handled = true; }
        else if (ctrl && e.Key is Key.D2 or Key.NumPad2) { Navigate("tasks"); e.Handled = true; }
        else if (ctrl && e.Key is Key.D3 or Key.NumPad3) { Navigate("topics"); e.Handled = true; }
        else if (ctrl && e.Key is Key.D4 or Key.NumPad4) { Navigate("links"); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { current?.New(); e.Handled = true; }
    }

    // ================================================================== commands
    void QuickCaptureClick(object sender, RoutedEventArgs e) => App.Current.QuickCapture();
    void ExitClick(object sender, RoutedEventArgs e) => App.Current.ExitApp();
    void BackupNowClick(object sender, RoutedEventArgs e) { }
    void DataFolderClick(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(DataFolder.Root);
        try { Process.Start("explorer.exe", "\"" + DataFolder.Root + "\""); }
        catch (System.ComponentModel.Win32Exception) { }
    }
    void StartupClick(object sender, RoutedEventArgs e) { App.StartupEnabled = StartupMenu.IsChecked; StartupMenu.IsChecked = App.StartupEnabled; }
    void ComponentsClick(object sender, RoutedEventArgs e) => _ = App.Current.CheckComponentsAsync();
    void AboutClick(object sender, RoutedEventArgs e) => AboutWindow.Show(this);
    void LangEnClick(object sender, RoutedEventArgs e) => SetLanguage("en");
    void LangEsClick(object sender, RoutedEventArgs e) => SetLanguage("es");
    void SetLanguage(string lang)
    {
        LangEnMenu.IsChecked = lang == "en"; LangEsMenu.IsChecked = lang == "es";
        if (lang == L.Lang) return;
        if (PromptWindow.Confirm(this, L.T("Idioma"), L.T("BuzzDesk se reinicia para cambiar el idioma."), L.T("Reiniciar"), L.T("Cancelar"))) App.Current.SetLanguage(lang);
        else { LangEnMenu.IsChecked = !L.Es; LangEsMenu.IsChecked = L.Es; }
    }
}
