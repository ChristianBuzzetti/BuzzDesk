using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>A list of tasks with its editor: the inbox, the next 7 days, someday (no date), all, done and each
/// area or project. An add box that understands "!3 mañana #tag" sits on top; the list is grouped by day.</summary>
public sealed class TasksView : DeskView
{
    readonly string key;
    public override string Id => key;
    /// <summary>The views this class serves: key, title, list.</summary>
    public static readonly (string key, string title, TaskScope scope)[] Kinds =
    [
        ("inbox", "Bandeja de entrada", TaskScope.Inbox),
        ("upcoming", "Próximos 7 días", TaskScope.Week),
        ("someday", "Algún día", TaskScope.NoDate),
        ("tasks", "Todos los pendientes", TaskScope.Open),
        ("done", "Completados", TaskScope.Done),
    ];
    readonly string baseTitle;
    readonly TextBlock heading = Ui.Title("");
    readonly TextBlock subheading = Ui.Text("", "Muted", 12, mono: true);
    readonly WrapPanel scopes = new() { Margin = new Thickness(0, 10, 0, 4) };
    readonly TextBox add = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TaskEditor editor = new();
    readonly TextBlock emptyText = Ui.Text("", "Muted");
    TaskScope scope = TaskScope.Open;
    long? sectionId;
    long? selectId;

    readonly TaskScope fixedScope;
    readonly ColumnDefinition editorColumn = new() { Width = new GridLength(400) };

    public TasksView(string key = "tasks")
    {
        this.key = key;
        var kind = Kinds.FirstOrDefault(k => k.key == key);
        if (kind.key == null) kind = Kinds[3];
        baseTitle = L.T(kind.title);
        fixedScope = scope = kind.scope;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(editorColumn);
        var left = new DockPanel { Margin = new Thickness(8, 4, 16, 0) };
        var head = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(heading);
        subheading.Margin = new Thickness(12, 8, 0, 0);
        titleRow.Children.Add(subheading);
        head.Children.Add(titleRow);
        // "All tasks" (and an area or project) can switch between open and done; the other lists are fixed.
        if (key == "tasks")
            foreach (var (label, s) in new[] { (L.T("Abiertos"), TaskScope.Open), (L.T("Completados"), TaskScope.Done) })
            {
                var chip = new RadioButton { Content = label, GroupName = "TaskScope" + key, IsChecked = s == scope, Tag = s };
                chip.SetResourceReference(StyleProperty, "SegmentChip");
                chip.Checked += (_, _) => { scope = (TaskScope)chip.Tag; Refresh(); };
                scopes.Children.Add(chip);
            }
        else scopes.Margin = new Thickness(0, 6, 0, 0);
        head.Children.Add(scopes);
        add.KeyDown += AddKeyDown;
        add.ToolTip = L.T("Enter para agregar. Entiende: !1 !2 !3 (prioridad), hoy, mañana, viernes, 25/11, 18:30, #etiqueta");
        var addBox = TaskEditor.Placeholder(add, key switch
        {
            "upcoming" => L.T("+ Agregar pendiente   (ej.: pagar el alquiler viernes !2)"),
            "someday" => L.T("+ Agregar idea o pendiente sin fecha"),
            _ => L.T("+ Agregar pendiente   (ej.: revisar backups mañana 9:30 #dba)"),
        });
        if (key == "done") addBox.Visibility = Visibility.Collapsed;
        head.Children.Add(addBox);
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        emptyText.Margin = new Thickness(4, 24, 0, 0);
        DockPanel.SetDock(emptyText, Dock.Top);
        left.Children.Add(emptyText);
        list.Margin = new Thickness(0, 6, 0, 0);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Content: FrameworkElement { Tag: TaskItem t } }) editor.Show(t.Id); };
        list.KeyDown += ListKeyDown;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        left.Children.Add(list);
        Children.Add(left);
        SetColumn(editor, 1);
        editor.Margin = new Thickness(0, 0, 0, 0);
        Children.Add(editor);
        SizeChanged += (_, _) => { bool room = ActualWidth >= 860; editor.Visibility = room ? Visibility.Visible : Visibility.Collapsed; editorColumn.Width = new GridLength(room ? 400 : 0); };
    }

    public override void BuildToolbar(Panel bar)
    {
        bar.Children.Add(Ui.ToolButton("", L.T("Nuevo pendiente"), (_, _) => New(), "Ctrl+N"));
        if (key == "tasks") bar.Children.Add(Ui.ToolButton("", L.T("Nuevo proyecto"), (_, _) => Host.NewSection(null)));
    }

    public override void New() { add.Focus(); }

    /// <summary>Filters by an area or project (null = all) and shows the list.</summary>
    public void SetSection(long? id) { sectionId = id; Refresh(); }
    public long? SectionId => sectionId;

    /// <summary>Selects a task (from Today, search, a reminder), switching to a scope that shows it.</summary>
    public void Select(long id)
    {
        var t = Tasks.Get(id);
        if (t == null) return;
        if (t.ParentId is long parent) { Select(parent); return; }
        if (sectionId != null && t.SectionId != sectionId && Sections.Get(t.SectionId ?? 0)?.ParentId != sectionId) sectionId = null;
        var needed = t.IsOpen ? TaskScope.Open : TaskScope.Done;
        if (key == "tasks" && scope != needed) SetScope(needed);
        selectId = id;
        Refresh();
        editor.Show(id);
    }

    void SetScope(TaskScope s)
    {
        scope = s;
        foreach (RadioButton chip in scopes.Children) chip.IsChecked = (TaskScope)chip.Tag == s;
    }

    public override void Refresh()
    {
        var today = Clock.Today;
        var section = sectionId is long sid ? Sections.Get(sid) : null;
        if (sectionId != null && section == null) sectionId = null;
        heading.Text = section?.Name ?? baseTitle;
        long? keep = selectId ?? editor.TaskId;
        selectId = null;
        var items = Tasks.List(new TaskFilter(scope, sectionId, Limit: scope == TaskScope.Done ? 200 : 1000), today);
        subheading.Text = L.Count(items.Count, "pendiente", "pendientes");
        list.Items.Clear();
        foreach (var (label, brush, group) in scope == TaskScope.Week ? WeekGroups(items, today) : Group(items, today))
        {
            list.Items.Add(TaskRows.Header(label, group.Count, brush));
            foreach (var t in group) list.Items.Add(new ListBoxItem { Content = TaskRows.Build(t, today, x => editor.Show(x.Id), showSection: sectionId == null), Padding = new Thickness(0) });
            if (group.Count == 0)
            {
                var none = Ui.Text(L.T("Nada planificado"), "LineNumber", 12);
                none.Margin = new Thickness(10, 2, 0, 6);
                list.Items.Add(new ListBoxItem { Content = none, Focusable = false, IsHitTestVisible = false });
            }
        }
        bool shown = items.Count > 0 || scope == TaskScope.Week;
        emptyText.Text = shown ? "" : scope switch
        {
            TaskScope.Done => L.T("Todavía no completaste nada acá."),
            TaskScope.Inbox => L.T("Tu bandeja está vacía. Lo que anotes sin proyecto llega acá para ordenarlo después."),
            TaskScope.NoDate => L.T("Nada sin fecha. Las ideas y pendientes \"para algún día\" van acá."),
            _ => L.T("Sin pendientes. Escribí uno arriba y Enter."),
        };
        emptyText.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        if (keep is long id)
        {
            var row = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Content is FrameworkElement { Tag: TaskItem t } && t.Id == id);
            if (row != null) { list.SelectedItem = row; row.BringIntoView(); }
            if (editor.TaskId == id) editor.Reload(); else if (row != null) editor.Show(id);
        }
    }

    static IEnumerable<(string label, string brush, List<TaskItem> items)> Group(List<TaskItem> items, DateOnly today)
    {
        if (items.Count == 0) yield break;
        if (items[0].Status == TaskState.Done)
        {
            foreach (var g in items.GroupBy(t => DateOnly.FromDateTime(Clock.ToLocal(t.CompletedUtc ?? t.UpdatedUtc))))
                yield return (Fmt.Day(g.Key, today), "Muted", g.ToList());
            yield break;
        }
        var weekEnd = today.AddDays(7);
        (string, string, Func<TaskItem, bool>)[] buckets =
        [
            (L.T("Atrasados"), "ErrorInk", t => t.DueDate < today),
            (L.T("Hoy"), "Accent", t => t.DueDate == today),
            (L.T("Mañana"), "Muted", t => t.DueDate == today.AddDays(1)),
            (L.T("Próximos 7 días"), "Muted", t => t.DueDate > today.AddDays(1) && t.DueDate <= weekEnd),
            (L.T("Más adelante"), "Muted", t => t.DueDate > weekEnd),
            (L.T("Sin fecha"), "Muted", t => t.DueDate == null),
        ];
        foreach (var (label, brush, match) in buckets)
        {
            var g = items.Where(match).ToList();
            if (g.Count > 0) yield return (label, brush, g);
        }
    }

    /// <summary>The next 7 days, one header per day (empty days too), overdue first.</summary>
    static IEnumerable<(string label, string brush, List<TaskItem> items)> WeekGroups(List<TaskItem> items, DateOnly today)
    {
        var late = items.Where(t => t.DueDate < today).ToList();
        if (late.Count > 0) yield return (L.T("Atrasados"), "ErrorInk", late);
        for (int i = 0; i < 7; i++)
        {
            var day = today.AddDays(i);
            string name = i <= 1 ? Fmt.Day(day, today) + " · " + Fmt.Cap(day.ToString("ddd d MMM", L.Culture)) : Fmt.Cap(day.ToString("dddd d MMM", L.Culture));
            yield return (name, i == 0 ? "Accent" : "Muted", items.Where(t => t.DueDate == day).ToList());
        }
    }

    void AddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { add.Text = ""; e.Handled = true; return; }
        if (e.Key != Key.Enter || add.Text.Trim().Length == 0) return;
        var p = QuickParse.Parse(add.Text, Clock.Today);
        if (p.Title.Length == 0) return;
        // Upcoming without a date means today; someday and the inbox keep what was typed.
        var due = p.Date ?? (scope == TaskScope.Week ? Clock.Today : null);
        long id = Tasks.Add(new Tasks.Draft(p.Title, sectionId, p.Priority ?? 0, due, p.Time, Tags: p.Tags), Clock.Today);
        add.Text = "";
        if (key == "tasks" && scope == TaskScope.Done) SetScope(TaskScope.Open);
        selectId = id;
        Refresh();
        add.Focus();
        e.Handled = true;
    }

    void ListKeyDown(object sender, KeyEventArgs e)
    {
        if (list.SelectedItem is not ListBoxItem { Content: FrameworkElement { Tag: TaskItem t } }) return;
        if (e.Key == Key.Space) { if (t.IsOpen) Actions.Complete(t); else Actions.Reopen(t); e.Handled = true; }
        else if (e.Key == Key.Delete) { Actions.Delete(t); e.Handled = true; }
        else if (e.Key == Key.Enter) { editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); e.Handled = true; }
    }
}
