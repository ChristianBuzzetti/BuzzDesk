using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>Topics: the list (to review first) and the editor with the timeline.</summary>
public sealed class TopicsView : DeskView
{
    public override string Id => "topics";
    readonly TextBlock subheading = Ui.Text("", "Muted", 12, mono: true);
    readonly WrapPanel scopes = new() { Margin = new Thickness(0, 10, 0, 4) };
    readonly TextBox add = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TopicEditor editor = new();
    readonly TextBlock emptyText = Ui.Text("", "Muted");
    TopicScope scope = TopicScope.Open;
    long? selectId;

    public TopicsView()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(8, 4, 16, 0) };
        var head = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(Ui.Title(L.T("Temas")));
        subheading.Margin = new Thickness(12, 6, 0, 0);
        titleRow.Children.Add(subheading);
        head.Children.Add(titleRow);
                foreach (var (label, s) in new[] { (L.T("Abiertos"), TopicScope.Open), (L.T("Para revisar"), TopicScope.Review), (L.T("Esperando"), TopicScope.Waiting), (L.T("En pausa"), TopicScope.Paused), (L.T("Cerrados"), TopicScope.Closed) })
        {
            var chip = new RadioButton { Content = label, GroupName = "TopicScope", IsChecked = s == scope, Tag = s };
            chip.SetResourceReference(StyleProperty, "SegmentChip");
            chip.Checked += (_, _) => { scope = (TopicScope)chip.Tag; Refresh(); };
            scopes.Children.Add(chip);
        }
        head.Children.Add(scopes);
        add.KeyDown += AddKeyDown;
        head.Children.Add(TaskEditor.Placeholder(add, L.T("Nuevo tema…  (Enter)")));
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        emptyText.Margin = new Thickness(4, 24, 0, 0);
        DockPanel.SetDock(emptyText, Dock.Top);
        left.Children.Add(emptyText);
        list.Margin = new Thickness(0, 6, 0, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: Topic t }) editor.Show(t.Id); };
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); e.Handled = true; } };
        left.Children.Add(list);
        Children.Add(left);
        SetColumn(editor, 1);
        Children.Add(editor);
    }

    public override void BuildToolbar(Panel bar) => bar.Children.Add(Ui.ToolButton("", L.T("Nuevo tema"), (_, _) => New(), "Ctrl+N"));
    public override void New() => add.Focus();

    public void Select(long id)
    {
        var t = Topics.Get(id);
        if (t == null) return;
        var needed = t.Status == TopicState.Closed ? TopicScope.Closed : TopicScope.Open;
        if (!(scope == needed || scope == TopicScope.Review && t.ReviewDue(Clock.Today) || scope == TopicScope.Waiting && t.Status == TopicState.Waiting || scope == TopicScope.Paused && t.Status == TopicState.Paused))
        {
            scope = needed;
            foreach (RadioButton chip in scopes.Children) chip.IsChecked = (TopicScope)chip.Tag == scope;
        }
        selectId = id;
        Refresh();
        editor.Show(id);
    }

    public override void Refresh()
    {
        var today = Clock.Today;
        long? keep = selectId ?? editor.TopicId;
        selectId = null;
        var items = Topics.List(scope, today);
        subheading.Text = L.Count(items.Count, "tema", "temas");
        list.Items.Clear();
        foreach (var t in items) list.Items.Add(new ListBoxItem { Content = Row(t, today), Tag = t, Padding = new Thickness(0) });
        emptyText.Text = items.Count > 0 ? "" : scope switch
        {
            TopicScope.Review => L.T("Nada para revisar hoy."),
            TopicScope.Waiting => L.T("No estás esperando a nadie."),
            _ => L.T("Un tema es algo que avanza en el tiempo: un incidente, un pedido a otra área, una migración. Escribí uno arriba."),
        };
        emptyText.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (keep is long id)
        {
            var row = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is Topic t && t.Id == id);
            if (row != null) { list.SelectedItem = row; row.BringIntoView(); }
            if (editor.TopicId == id) editor.Reload();
        }
    }

    /// <summary>Status dot, title, and what matters at a glance: who we wait on, review date, open tasks.</summary>
    public static FrameworkElement Row(Topic t, DateOnly today)
    {
        var grid = new Grid { MinHeight = 46, Background = System.Windows.Media.Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Margin = new Thickness(10, 0, 0, 0), ToolTip = StatusName(t.Status) };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, StatusBrush(t.Status));
        grid.Children.Add(dot);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 6, 8, 6) };
        var title = Ui.Text(t.Title, t.Status == TopicState.Closed ? "Muted" : "Ink", weight: FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        var meta = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (t.Status == TopicState.Waiting && t.WaitingOn != null)
        {
            int days = t.WaitingSinceUtc is DateTime since ? (int)(DateTime.UtcNow - since).TotalDays : 0;
            meta.Children.Add(Ui.Chip("⏳ " + t.WaitingOn + (days > 0 ? " · " + days + " d" : ""), days >= 7 ? "AccentWeak" : "Badge", days >= 7 ? "Accent" : "BadgeInk"));
        }
        if (t.SectionName != null) meta.Children.Add(Ui.Chip(t.SectionName));
        foreach (var tag in t.Tags) meta.Children.Add(Ui.Chip("#" + tag, "AlternateRow", "Muted"));
        if (t.OpenTasks > 0) meta.Children.Add(Small("☐ " + t.OpenTasks));
        if (t.Notes > 0) meta.Children.Add(Small("✎ " + t.Notes));
        if (meta.Children.Count > 0) text.Children.Add(meta);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        string right = t.ReviewDue(today) ? L.T("Revisar hoy") : t.NextReview is DateOnly d && t.Status != TopicState.Closed ? Fmt.Day(d, today) : t.LastNoteUtc is DateTime last ? Fmt.Ago(last) : "";
        var when = Ui.Text(right, t.ReviewDue(today) ? "Accent" : "Muted", 12, mono: true);
        when.Margin = new Thickness(8, 0, 10, 0);
        Grid.SetColumn(when, 2);
        grid.Children.Add(when);
        return grid;
    }

    static TextBlock Small(string text) { var t = Ui.Text(text, "Muted", 11, mono: true); t.Margin = new Thickness(0, 0, 8, 0); return t; }

    public static string StatusBrush(TopicState s) => s switch { TopicState.Active => "Accent", TopicState.Waiting => "SqlNumber", TopicState.Paused => "LineNumber", _ => "StrongBorder" };
    public static string StatusName(TopicState s) => s switch { TopicState.Active => L.T("Activo"), TopicState.Waiting => L.T("Esperando"), TopicState.Paused => L.T("En pausa"), _ => L.T("Cerrado") };

    void AddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { add.Text = ""; e.Handled = true; return; }
        if (e.Key != Key.Enter || add.Text.Trim().Length == 0) return;
        var p = Domain.QuickParse.Parse(add.Text, Clock.Today);
        long id = Topics.Add(p.Title.Length > 0 ? p.Title : add.Text.Trim(), nextReview: p.Date, tags: p.Tags);
        add.Text = "";
        if (scope is TopicScope.Closed or TopicScope.Waiting or TopicScope.Paused || scope == TopicScope.Review && p.Date == null)
        {
            scope = TopicScope.Open;
            foreach (RadioButton chip in scopes.Children) chip.IsChecked = (TopicScope)chip.Tag == scope;
        }
        selectId = id;
        Refresh();
        editor.Show(id);
        e.Handled = true;
    }

    /// <summary>Today's block: topics whose review date has come.</summary>
    public static FrameworkElement? TodayBlock(DateOnly today)
    {
        var due = Topics.List(TopicScope.Review, today);
        if (due.Count == 0) return null;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Label(L.T("Temas para revisar") + "  ·  " + due.Count, new Thickness(4, 14, 0, 6)));
        foreach (var t in due)
        {
            var row = Row(t, today);
            var frame = new Border { Child = row, Margin = new Thickness(0, 0, 0, 2), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            frame.SetResourceReference(Border.BackgroundProperty, "Surface");
            frame.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
            frame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
            long id = t.Id;
            frame.MouseLeftButtonUp += (_, _) => App.Current.Desk?.OpenTopic(id);
            stack.Children.Add(frame);
        }
        return stack;
    }
}
