using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuzzDesk.Data;

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

        Db.Changed += what => ScheduleRefresh(what == "section");
        TodayView.Blocks.Add(HabitsView.TodayBlock);
        TodayView.Blocks.Add(ReminderUi.TodayBlock);
        TodayView.Blocks.Add(BillsView.TodayBlock);
        TodayView.Blocks.Add(DatesView.TodayBlock);
        TodayView.Blocks.Add(TopicsView.TodayBlock);
        TodayView.Blocks.Add(InboxUi.TodayBlock);
        TodayView.Blocks.Add(LinkUi.TodayBlock);
        App.Current.DayChanged += () => ScheduleRefresh(false);

        OnAppearance(appearance);
        RefreshShortcuts();
        ShowZone();
        SyncAiChip();
        RebuildSections();
        RefreshCounts();
        ShowCloud();
        Navigate(Store.Settings.LastNav);
    }

    // ================================================================== data changes
    bool refreshQueued, sectionsDirty;
    /// <summary>Coalesces bursts of writes into one refresh of the visible view, the counts and the sidebar.</summary>
    void ScheduleRefresh(bool sections)
    {
        sectionsDirty |= sections;
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            refreshQueued = false;
            current?.Refresh();
            RefreshCounts();
            if (sectionsDirty) { sectionsDirty = false; RebuildSections(); }
            else UpdateSectionCounts();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void RefreshCounts()
    {
        var (dueToday, overdue, open) = Tasks.Counts(Clock.Today);
        NavTodayCount.Text = dueToday + overdue > 0 ? (dueToday + overdue).ToString() : "";
        NavTodayCount.SetResourceReference(TextBlock.ForegroundProperty, overdue > 0 ? "ErrorInk" : "LineNumber");
        NavTasksCount.Text = open > 0 ? open.ToString() : "";
        var habits = Habits.List(Clock.Today).Where(h => h.AppliesOn(Clock.Today)).ToList();
        NavHabitsCount.Text = habits.Count > 0 ? habits.Count(h => h.DoneToday) + "/" + habits.Count : "";
        var lists = Checklists.List();
        int pendingItems = lists.Sum(l => l.Items - l.Done);
        NavListsCount.Text = lists.Count == 0 ? "" : pendingItems > 0 ? pendingItems.ToString() : lists.Count.ToString();
        var today0 = Clock.Today;
        var dueBills = Bills.List().Count(b => b.NextDue != null && (b.Overdue(today0) || b.DueSoon(today0)));
        NavBillsCount.Text = dueBills > 0 ? dueBills.ToString() : "";
        NavBillsCount.SetResourceReference(TextBlock.ForegroundProperty, dueBills > 0 ? "Accent" : "LineNumber");
        int soonDates = Dates.Upcoming(today0, 7).Count;
        NavDatesCount.Text = soonDates > 0 ? soonDates.ToString() : "";
        int links = Links.Count();
        NavLinksCount.Text = links > 0 ? links.ToString() : "";
        var (review, _, openTopics) = Topics.Counts(Clock.Today);
        NavTopicsCount.Text = review > 0 ? review + " / " + openTopics : openTopics > 0 ? openTopics.ToString() : "";
        NavTopicsCount.SetResourceReference(TextBlock.ForegroundProperty, review > 0 ? "Accent" : "LineNumber");
        NavTopics.ToolTip = review > 0 ? L.F("{0} para revisar hoy", review) : null;
        DbStatus.Text = "buzzdesk.db · v" + Migrator.All.Count;
    }

    // ================================================================== areas and projects in the sidebar
    readonly Dictionary<long, (RadioButton button, TextBlock count)> sectionNav = [];

    void RebuildSections()
    {
        NavSectionsGroup.Children.Clear();
        sectionNav.Clear();
        var label = new TextBlock { Text = L.T("ÁREAS Y PROYECTOS") };
        label.SetResourceReference(StyleProperty, "NavLabel");
        NavSectionsGroup.Children.Add(label);
        foreach (var s in Sections.All())
        {
            var count = new TextBlock { Text = s.OpenTasks > 0 ? s.OpenTasks.ToString() : "" };
            count.SetResourceReference(StyleProperty, "NavCount");
            DockPanel.SetDock(count, Dock.Right);
            var glyph = new TextBlock { Text = s.IsArea ? "\uE8B7" : "\uE8F1" };
            glyph.SetResourceReference(StyleProperty, "NavGlyph");
            var name = new TextBlock { Text = s.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            var row = new DockPanel { Margin = new Thickness(s.IsArea ? 0 : 18, 0, 0, 0) };
            row.Children.Add(count); row.Children.Add(glyph); row.Children.Add(name);
            var button = new RadioButton { Content = row, GroupName = "Nav", Tag = "section:" + s.Id, ToolTip = s.Name };
            button.SetResourceReference(StyleProperty, "NavItem");
            button.Checked += NavChecked;
            button.ContextMenu = SectionMenu(s);
            NavSectionsGroup.Children.Add(button);
            sectionNav[s.Id] = (button, count);
        }
        var add = new Button { Content = NavRow("\uE710", L.T("Nueva área")) };
        add.SetResourceReference(StyleProperty, "NavAction");
        add.Click += (_, _) => NewSection(null);
        NavSectionsGroup.Children.Add(add);
        SyncNavSelection();
    }

    void UpdateSectionCounts()
    {
        foreach (var s in Sections.All())
            if (sectionNav.TryGetValue(s.Id, out var nav)) nav.count.Text = s.OpenTasks > 0 ? s.OpenTasks.ToString() : "";
    }

    static DockPanel NavRow(string glyph, string text)
    {
        var g = new TextBlock { Text = glyph };
        g.SetResourceReference(StyleProperty, "NavGlyph");
        var row = new DockPanel();
        row.Children.Add(g); row.Children.Add(new TextBlock { Text = text });
        return row;
    }

    ContextMenu SectionMenu(Section s)
    {
        var menu = new ContextMenu();
        void Add(string header, Action click) { var m = new MenuItem { Header = header }; m.Click += (_, _) => click(); menu.Items.Add(m); }
        if (s.IsArea) Add(L.T("Nuevo proyecto…"), () => NewSection(s.Id));
        Add(L.T("Renombrar…"), () =>
        {
            if (PromptWindow.Ask(this, L.T("Renombrar"), L.T("Nuevo nombre:"), s.Name) is { } name && name.Trim().Length > 0) TrySection(() => Sections.Rename(s.Id, name));
        });
        Add(L.T("Archivar"), () =>
        {
            if (PromptWindow.Confirm(this, L.T("Archivar"), L.F("¿Archivar \"{0}\"? Sus pendientes se conservan.", s.Name), L.T("Archivar"), L.T("Cancelar")))
                Sections.Archive(s.Id, true);
        });
        return menu;
    }

    public void NewSection(long? parentId)
    {
        string title = parentId == null ? L.T("Nueva área") : L.T("Nuevo proyecto");
        if (PromptWindow.Ask(this, title, L.T("Nombre:"), "") is not { } name || name.Trim().Length == 0) return;
        TrySection(() => Sections.Add(name, parentId));
    }

    void TrySection(Action work)
    {
        try { work(); }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)   // SQLITE_CONSTRAINT: the name exists
        { PromptWindow.Info(this, L.T("Ya existe"), L.T("Ya hay un área o proyecto con ese nombre.")); }
    }

    /// <summary>Opens a task in Tasks (from Today, search or a reminder).</summary>
    public void OpenTask(long id)
    {
        Navigate("tasks");
        ((TasksView)views["tasks"]).Select(id);
        SyncNavSelection();
    }

    void SyncNavSelection()
    {
        if (current is TasksView { SectionId: long sid } && sectionNav.TryGetValue(sid, out var nav)) { if (nav.button.IsChecked != true) nav.button.IsChecked = true; }
        else if (current is TasksView && NavTasks.IsChecked != true) NavTasks.IsChecked = true;
    }

    // ================================================================== navigation
    DeskView CreateView(string key) => key switch
    {
        "tasks" => new TasksView(),
        "topics" => new TopicsView(),
        "links" => new LinksView(),
        "search" => new SearchView(),
        "habits" => new HabitsView(),
        "lists" => new ListsView(),
        "bills" => new BillsView(),
        "dates" => new DatesView(),
        _ => new TodayView(),
    };

    public void Navigate(string key)
    {
        if (key is not ("today" or "tasks" or "topics" or "links" or "search" or "habits" or "lists" or "bills" or "dates")) key = "today";
        if (!views.TryGetValue(key, out var view)) { view = CreateView(key); views[key] = view; }
        current = view;
        ViewHost.Content = view;
        ViewToolbar.Children.Clear();
        view.BuildToolbar(ViewToolbar);
        view.Shown();
        var nav = key switch { "tasks" => NavTasks, "topics" => NavTopics, "links" => NavLinks, "search" => NavSearch, "habits" => NavHabits, "lists" => NavLists, "bills" => NavBills, "dates" => NavDates, _ => NavToday };
        if (view is TasksView { SectionId: long sid } && sectionNav.TryGetValue(sid, out var sn)) { if (sn.button.IsChecked != true) sn.button.IsChecked = true; }
        else if (nav.IsChecked != true) nav.IsChecked = true;
        if (key != "search" && Store.Settings.LastNav != key) { Store.Settings.LastNav = key; Store.SaveSettings(); }
    }

    void NavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string key }) return;
        if (key.StartsWith("section:", StringComparison.Ordinal))
        {
            long id = long.Parse(key[8..]);
            if (current is not TasksView) Navigate("tasks");
            ((TasksView)views["tasks"]).SetSection(id);
            return;
        }
        if (key == "tasks" && views.TryGetValue("tasks", out var tv) && tv is TasksView { SectionId: not null } tasks) { tasks.SetSection(null); if (current == tasks) return; }
        if (current?.Id != key) Navigate(key);
    }

    void GoTodayClick(object sender, RoutedEventArgs e) => Navigate("today");
    void GoTasksClick(object sender, RoutedEventArgs e) => Navigate("tasks");
    void GoTopicsClick(object sender, RoutedEventArgs e) => Navigate("topics");
    void GoLinksClick(object sender, RoutedEventArgs e) => Navigate("links");

    /// <summary>Reloads the visible view (data changed elsewhere: quick capture, a reminder, another view).</summary>
    public void RefreshAll() { current?.Refresh(); }

    Action? statusAction;
    /// <summary>Footer message, optionally with an action button (Undo) that disappears with the next message.</summary>
    /// <summary>A small win: Boberto waves and the footer says it.</summary>
    public void Celebrate(string text)
    {
        SetStatus(text);
        if (views.TryGetValue("today", out var t) && t is TodayView today) today.Wave();
    }

    public void SetStatus(string text, string? actionLabel = null, Action? action = null)
    {
        StatusText.Text = text;
        statusAction = action;
        StatusActionText.Text = actionLabel ?? "";
        StatusAction.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
    }
    void StatusActionClick(object sender, RoutedEventArgs e) { var a = statusAction; SetStatus(L.T("Listo")); a?.Invoke(); }

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
        key = KeyName(key);
        StatusCaptureKey.Text = key;
        CaptureKeyHint.Text = key.Replace('+', ' ');
        MenuQuickCapture.InputGestureText = key;
    }

    /// <summary>"Ctrl+Alt+Space" as people read it in the app's language.</summary>
    public static string KeyName(string spec) => L.Es ? spec.Replace("Space", "Espacio") : spec;

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
    System.Windows.Threading.DispatcherTimer? searchDelay;
    string beforeSearch = "today";

    /// <summary>Typing searches everything (FTS5) after a short pause; clearing the box goes back.</summary>
    void SearchChanged(object sender, TextChangedEventArgs e)
    {
        searchDelay ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        searchDelay.Stop();
        searchDelay.Tick -= RunSearch; searchDelay.Tick += RunSearch;
        searchDelay.Start();
    }

    void RunSearch(object? sender, EventArgs e)
    {
        searchDelay!.Stop();
        string text = Search.Text.Trim();
        if (text.Length == 0)
        {
            NavSearch.Visibility = Visibility.Collapsed;
            if (current is SearchView) Navigate(beforeSearch);
            return;
        }
        if (current is not SearchView) beforeSearch = current?.Id ?? "today";
        NavSearch.Visibility = Visibility.Visible;
        if (current is not SearchView) Navigate("search");
        ((SearchView)views["search"]).SetQuery(text);
    }

    void FocusSearchClick(object sender, RoutedEventArgs e) { Search.Focus(); Search.SelectAll(); }

    /// <summary>Screenshots: types in the search box and runs it at once.</summary>
    internal void ShotSearch(string text) { Search.Text = text; RunSearch(null, EventArgs.Empty); }

    /// <summary>Opens a list (from search or quick capture).</summary>
    public void OpenList(long id)
    {
        Navigate("lists");
        ((ListsView)views["lists"]).Select(id);
    }

    /// <summary>Selects a link in Links (from search).</summary>
    public void OpenLink(long id)
    {
        Navigate("links");
        ((LinksView)views["links"]).Select(id);
    }

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
            case "launcher": LauncherWindow.Toggle(); break;
            case "habits": case "lists": case "bills": case "dates": Navigate(action); Activate(); break;
            case "plan": Navigate("today"); Activate(); DayPlanUi.Pick(this); break;
            case "cloud": CloudUi.Configure(this); break;
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
        if (Search.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Escape) { Search.Text = ""; e.Handled = true; return; }
            if (e.Key == Key.Enter && views.TryGetValue("search", out var sv)) { searchDelay?.Stop(); RunSearch(null, EventArgs.Empty); ((SearchView)sv).OpenFirst(); e.Handled = true; return; }
            if (e.Key == Key.Down && current is SearchView s1) { s1.FocusList(); e.Handled = true; return; }
        }
        if (ctrl && e.Key is Key.D1 or Key.NumPad1) { Navigate("today"); e.Handled = true; }
        else if (ctrl && e.Key is Key.D2 or Key.NumPad2) { Navigate("tasks"); e.Handled = true; }
        else if (ctrl && e.Key is Key.D3 or Key.NumPad3) { Navigate("topics"); e.Handled = true; }
        else if (ctrl && e.Key is Key.D4 or Key.NumPad4) { Navigate("links"); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { current?.New(); e.Handled = true; }
        else if (ctrl && e.Key == Key.L) { LauncherWindow.Toggle(); e.Handled = true; }
    }

    // ================================================================== commands
    void QuickCaptureClick(object sender, RoutedEventArgs e) => App.Current.QuickCapture();
    void ExitClick(object sender, RoutedEventArgs e) => App.Current.ExitApp();
    void BackupNowClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Backup.Result r;
            using (var c = Db.Open()) r = Backup.Run(c, Db.BackupDir, "manual", Clock.Now);
            SetStatus(L.F("Copia de seguridad verificada: {0} ({1:N0} KB)", System.IO.Path.GetFileName(r.Path), r.Bytes / 1024));
        }
        catch (Exception ex) when (ex is System.IO.IOException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException or System.IO.InvalidDataException)
        { PromptWindow.Info(this, L.T("No se pudo hacer la copia de seguridad"), ex.Message); }
    }
    void DataFolderClick(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(DataFolder.Root);
        try { Process.Start("explorer.exe", "\"" + DataFolder.Root + "\""); }
        catch (System.ComponentModel.Win32Exception) { }
    }
    void StartupClick(object sender, RoutedEventArgs e) { App.StartupEnabled = StartupMenu.IsChecked; SyncStartupMenu(); }
    public void SyncStartupMenu() => StartupMenu.IsChecked = App.StartupEnabled;

    /// <summary>Opens a topic in Topics (from Today, a task, search or a reminder).</summary>
    public void OpenTopic(long id)
    {
        Navigate("topics");
        ((TopicsView)views["topics"]).Select(id);
    }
    void CloudClick(object sender, RoutedEventArgs e) => CloudUi.Configure(this);
    void CloudNowClick(object sender, RoutedEventArgs e) => CloudUi.UploadNow();
    void RestoreClick(object sender, RoutedEventArgs e) => CloudUi.Restore(this);

    /// <summary>Footer: where the cloud copies go and when the last one went.</summary>
    public void ShowCloud()
    {
        CloudStatus.Text = Cloud.Status();
        CloudStatus.SetResourceReference(TextBlock.ForegroundProperty, Store.Settings.CloudLastError.Length > 0 ? "ErrorInk" : Cloud.Enabled ? "Ink" : "Muted");
        CloudStatus.ToolTip = Store.Settings.CloudLastError.Length > 0 ? Store.Settings.CloudLastError : Store.Settings.CloudFolder;
    }

    void LogClick(object sender, RoutedEventArgs e) => App.OpenLog();

    void SupportClick(object sender, RoutedEventArgs e)
    {
        string info = App.Current.SupportInfo();
        try { Clipboard.SetText(info); } catch (System.Runtime.InteropServices.COMException) { }
        PromptWindow.Info(this, L.T("Datos para soporte copiados"), L.T("Pegalos donde quieras (mail, chat). No incluyen tus pendientes, notas ni enlaces.") + "\n\n" + info.Replace("  ", " "));
    }

    void ExportClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = L.T("Elegí dónde guardar los archivos CSV"), UseDescriptionForTitle = true,
            SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        string folder = System.IO.Path.Combine(dlg.SelectedPath, "BuzzDesk " + Clock.Now.ToString("yyyy-MM-dd HHmm"));
        var culture = App.WindowsCulture;
        string list = culture.TextInfo.ListSeparator;
        try
        {
            var files = Export.All(folder, new Export.Options(list.Length == 1 ? list[0] : ',', Clock.ToLocal, culture.NumberFormat.NumberDecimalSeparator));
            SetStatus(L.F("Exportados {0} archivos en {1}", files.Count, folder));
            Process.Start("explorer.exe", "\"" + folder + "\"");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Log.Error("export", ex);
            PromptWindow.Info(this, L.T("No se pudo exportar"), ex.Message);
        }
    }
    void OptionsClick(object sender, RoutedEventArgs e) => OptionsUi.Show(this);
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
