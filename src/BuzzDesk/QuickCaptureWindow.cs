using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>
/// Quick capture (Ctrl+Alt+Space anywhere): a small card next to the cursor, like BuzzPrint's quick menu, to save a
/// task, a note, a reminder or a link in two seconds. The text understands "!3 mañana 9:30 #tag"; a line below says
/// what will be saved. Enter saves, Tab changes the kind, Esc closes. An unsent draft comes back next time.
/// </summary>
public sealed class QuickCaptureWindow : Window
{
    static QuickCaptureWindow? open;
    static string draft = "";
    readonly TextBox box = new() { FontSize = 16, Padding = new Thickness(10, 9, 10, 9), BorderThickness = new Thickness(0), TextWrapping = TextWrapping.Wrap, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly StackPanel modes = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
    readonly ComboBox target = new() { MinWidth = 220 };
    readonly TextBlock targetLabel = Ui.Text("", "Muted", 12);
    readonly StackPanel targetRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock preview = Ui.Text("", "Muted", 12.5);
    readonly TextBlock title = Ui.Text("", "Muted", 10.5, FontWeights.Bold);
    string mode;
    bool closing, saved, userPickedMode, switching, shot;

    QuickCaptureWindow()
    {
        mode = Store.Settings.QuickMode is "task" or "note" or "reminder" or "link" ? Store.Settings.QuickMode : "task";
        SetResourceReference(StyleProperty, typeof(Window));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        Width = 540; SizeToContent = SizeToContent.Height;
        Title = L.T("Captura rápida") + " · BuzzDesk";

        var frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12) };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        frame.SetResourceReference(Border.BorderBrushProperty, "AccentRing");
        var root = new StackPanel();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var mark = new BobertoMark { Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0) };
        mark.SetResourceReference(BobertoMark.TileProperty, "Accent");
        head.Children.Add(mark);
        var keys = Ui.Text(Store.Settings.HotkeyCapture, "Muted", 11, mono: true);
        DockPanel.SetDock(keys, Dock.Right);
        head.Children.Add(keys);
        title.Text = L.T("CAPTURA RÁPIDA");
        head.Children.Add(title);
        root.Children.Add(head);

        foreach (var (key, label, glyph) in new[] { ("task", L.T("Pendiente"), ""), ("note", L.T("Nota"), ""), ("reminder", L.T("Recordatorio"), ""), ("link", L.T("Enlace"), "") })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 12, "Muted"); g.Margin = new Thickness(0, 0, 6, 0);
            g.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(RadioButton), 1) });
            content.Children.Add(g);
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var chip = new RadioButton { Content = content, GroupName = "QuickMode", Tag = key, IsChecked = key == mode, Focusable = false };
            chip.SetResourceReference(StyleProperty, "SegmentChip");
            chip.Checked += (_, _) => { if (switching) return; userPickedMode = true; SetMode((string)chip.Tag); box.Focus(); };
            modes.Children.Add(chip);
        }
        root.Children.Add(modes);

        var boxFrame = new Border { BorderThickness = new Thickness(1), Child = box };
        boxFrame.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
        boxFrame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
        boxFrame.SetResourceReference(Border.BackgroundProperty, "Surface");
        root.Children.Add(boxFrame);

        targetLabel.Margin = new Thickness(0, 0, 10, 0);
        targetRow.Children.Add(targetLabel);
        targetRow.Children.Add(target);
        root.Children.Add(targetRow);

        preview.Margin = new Thickness(2, 10, 0, 0);
        preview.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(preview);
        var hint = Ui.Text(L.T("Enter guarda · Tab cambia el tipo · Esc cierra · !1-!3 prioridad · mañana 9:30 · #etiqueta"), "LineNumber", 11);
        hint.Margin = new Thickness(2, 8, 0, 0);
        root.Children.Add(hint);
        frame.Child = root;
        Content = frame;

        box.Text = draft;
        box.TextChanged += (_, _) => { draft = box.Text; AutoMode(); UpdatePreview(); };
        box.PreviewKeyDown += Keys;
        target.SelectionChanged += (_, _) => UpdatePreview();
        Deactivated += (_, _) => { if (!shot) SafeClose(); };
        Closed += (_, _) => { if (open == this) open = null; };
        Loaded += (_, _) => { Activate(); box.Focus(); box.CaretIndex = box.Text.Length; };
        SetMode(mode);
    }

    /// <summary>For the screenshots: shown over the main window with sample text.</summary>
    internal static QuickCaptureWindow ForShot(string text, Window owner)
    {
        var w = new QuickCaptureWindow { WindowStartupLocation = WindowStartupLocation.Manual };
        w.Left = owner.Left + owner.Width / 2 - w.Width / 2; w.Top = owner.Top + 220;
        w.shot = true;
        w.box.Text = text;
        w.Show();
        return w;
    }

    public static void Toggle()
    {
        if (open != null) { open.SafeClose(); return; }
        var w = new QuickCaptureWindow();
        open = w;
        LauncherWindow.PlaceNearCursor(w);
        w.Show();
        w.Activate();
    }

    // ------------------------------------------------------------------ kinds
    void SetMode(string m)
    {
        mode = m;
        switching = true;
        try { foreach (RadioButton chip in modes.Children) if ((string)chip.Tag == m && chip.IsChecked != true) chip.IsChecked = true; }
        finally { switching = false; }
        target.Items.Clear();
        targetRow.Visibility = Visibility.Visible;
        if (m == "task")
        {
            targetLabel.Text = L.T("En");
            target.Items.Add(new ComboBoxItem { Content = L.T("(Sin área)"), Tag = 0L });
            foreach (var s in Sections.All()) target.Items.Add(new ComboBoxItem { Content = (s.IsArea ? "" : "    ") + s.Name, Tag = s.Id });
            target.SelectedItem = target.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (long)i.Tag == Store.Settings.QuickSectionId) ?? target.Items[0];
        }
        else if (m == "note")
        {
            targetLabel.Text = L.T("En el tema");
            target.Items.Add(new ComboBoxItem { Content = L.T("(Notas sueltas, en Hoy)"), Tag = 0L });
            foreach (var t in Topics.List(TopicScope.Open, Clock.Today)) target.Items.Add(new ComboBoxItem { Content = t.Title, Tag = t.Id });
            target.SelectedIndex = 0;
        }
        else targetRow.Visibility = Visibility.Collapsed;
        UpdatePreview();
    }

    /// <summary>A pasted address switches to Link unless the person chose the kind.</summary>
    void AutoMode()
    {
        if (userPickedMode) return;
        var p = QuickParse.Parse(box.Text, Clock.Today);
        if (p.Url != null && p.Title.Trim() == p.Url && mode != "link") SetMode("link");
        else if (p.Url == null && mode == "link") SetMode(Store.Settings.QuickMode is "link" ? "task" : Store.Settings.QuickMode);
    }

    void UpdatePreview()
    {
        string text = box.Text.Trim();
        if (text.Length == 0) { preview.Text = Placeholder(); preview.SetResourceReference(TextBlock.ForegroundProperty, "LineNumber"); return; }
        preview.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var p = QuickParse.Parse(text, Clock.Today);
        var parts = new List<string>();
        switch (mode)
        {
            case "task":
                parts.Add("☐ " + p.Title);
                if (p.Priority is int prio and > 0) parts.Add(Fmt.Priority(prio));
                if (p.Date is DateOnly d) parts.Add(L.T("vence") + " " + Fmt.Day(d, Clock.Today).ToLower(L.Culture));
                if (p.Time is TimeOnly t) parts.Add(L.F("aviso a las {0}", Fmt.Time(t)));
                break;
            case "note": parts.Add("✎ " + Short(text)); break;
            case "reminder":
                if (When(p) is DateTime utc) parts.Add("⏰ " + Fmt.When(utc) + " · " + (p.Title.Length > 0 ? p.Title : "?"));
                else { preview.Text = L.T("¿Cuándo? Agregá una hora o un día: 15:30, mañana 9, viernes."); preview.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); return; }
                break;
            case "link":
                if (p.Url == null || Links.Normalize(p.Url) == null) { preview.Text = L.T("Pegá una dirección (https://…)."); preview.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); return; }
                string name = p.Title.Replace(p.Url, "").Trim();
                parts.Add("🔗 " + (name.Length > 0 ? name + " · " : "") + p.Url);
                break;
        }
        if (mode != "note") foreach (var tag in p.Tags) parts.Add("#" + tag);
        preview.Text = string.Join("  ·  ", parts);
    }

    string Placeholder() => mode switch
    {
        "task" => L.T("Ej.: Revisar backups de PROD !3 mañana 9:30 #dba"),
        "note" => L.T("Ej.: Infra confirma la ventana del sábado 22 hs"),
        "reminder" => L.T("Ej.: Llamar a Mariela 15:30"),
        _ => L.T("Ej.: https://grafana.local/d/sql Grafana SQL #mon"),
    };

    static string Short(string s) => s.Length > 120 ? s[..120] + "…" : s.Replace('\n', ' ');

    /// <summary>A reminder's moment: the day (today if none) and time (9:00 if a day was given without a time).</summary>
    static DateTime? When(Parsed p)
    {
        if (p.Date == null && p.Time == null) return null;
        var utc = Clock.ToUtc(p.Date ?? Clock.Today, p.Time ?? new TimeOnly(9, 0));
        // "15:30" already past today means tomorrow.
        if (p.Date == null && utc <= DateTime.UtcNow) utc = Clock.ToUtc(Clock.Today.AddDays(1), p.Time!.Value);
        return utc;
    }

    // ------------------------------------------------------------------ keys and save
    void Keys(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (e.Key == Key.Escape) { draft = ""; SafeClose(); e.Handled = true; }
        else if (e.Key == Key.Tab) { userPickedMode = true; Cycle(shift ? -1 : 1); e.Handled = true; }
        else if (ctrl && e.Key is >= Key.D1 and <= Key.D4) { userPickedMode = true; SetMode(new[] { "task", "note", "reminder", "link" }[e.Key - Key.D1]); e.Handled = true; }
        else if (e.Key == Key.Enter && shift && mode == "note") { int i = box.CaretIndex; box.Text = box.Text.Insert(i, "\n"); box.CaretIndex = i + 1; e.Handled = true; }
        else if (e.Key == Key.Enter) { Save(); e.Handled = true; }
    }

    void Cycle(int step)
    {
        var all = new[] { "task", "note", "reminder", "link" };
        SetMode(all[(Array.IndexOf(all, mode) + step + all.Length) % all.Length]);
    }

    void Save()
    {
        string text = box.Text.Trim();
        if (text.Length == 0) return;
        var p = QuickParse.Parse(text, Clock.Today);
        long selected = target.SelectedItem is ComboBoxItem { Tag: long id } ? id : 0;
        string done;
        switch (mode)
        {
            case "task":
            {
                if (p.Title.Length == 0) return;
                long? section = selected > 0 ? selected : null;
                long taskId = Tasks.Add(new Tasks.Draft(p.Title, section, p.Priority ?? 0, p.Date ?? (p.Time != null ? Clock.Today : null), p.Time, Tags: p.Tags), Clock.Today);
                if (p.Time is TimeOnly t)
                {
                    var at = Clock.ToUtc(p.Date ?? Clock.Today, t);
                    if (at > DateTime.UtcNow) Reminders.Add(at, taskId: taskId);
                }
                Store.Settings.QuickSectionId = selected;
                done = L.F("Pendiente guardado: {0}", p.Title);
                break;
            }
            case "note":
                Topics.AddNote(selected > 0 ? selected : null, text);
                done = L.T("Nota guardada");
                break;
            case "reminder":
            {
                if (When(p) is not DateTime utc || p.Title.Length == 0) { UpdatePreview(); return; }
                Reminders.Add(utc, title: p.Title);
                done = L.F("Recordatorio: {0} · {1}", p.Title, Fmt.When(utc));
                App.Current.OfferStartup(null);
                break;
            }
            default:
            {
                if (p.Url == null || Links.Normalize(p.Url) is not string url) { UpdatePreview(); return; }
                string name = p.Title.Replace(p.Url, "").Trim();
                LinkUi.Save(url, name.Length > 0 ? name : null, p.Tags);
                done = L.T("Enlace guardado");
                break;
            }
        }
        Store.Settings.QuickMode = mode;
        Store.SaveSettings();
        draft = "";
        saved = true;
        App.Current.Desk?.SetStatus(done);
        // A beat of confirmation, then out of the way.
        title.Text = "✓ " + done.ToUpper(L.Culture);
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ok");
        box.IsEnabled = false;
        var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        t2.Tick += (_, _) => { t2.Stop(); SafeClose(); };
        t2.Start();
    }

    void SafeClose()
    {
        if (closing) return;
        if (!saved && !IsActive && box.Text.Length > 0) draft = box.Text;   // clicked away: keep the draft
        closing = true;
        open = null;
        Close();
    }
}
