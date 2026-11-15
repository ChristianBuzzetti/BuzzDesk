using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>Tasks: an add box that understands "!3 mañana #tag", the list grouped by when it's due, and the editor.</summary>
public sealed class TasksView : DeskView
{
    public override string Id => "tasks";
    readonly TextBlock heading = Ui.Title(L.T("Pendientes"));
    readonly TextBlock subheading = Ui.Text("", "Muted", 12, mono: true);
    readonly WrapPanel scopes = new() { Margin = new Thickness(0, 10, 0, 4) };
    readonly TextBox add = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TaskEditor editor = new();
    readonly TextBlock emptyText = Ui.Text("", "Muted");
    TaskScope scope = TaskScope.Open;
    long? sectionId;
    long? selectId;

    public TasksView()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        var left = new DockPanel { Margin = new Thickness(8, 4, 16, 0) };
        var head = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(heading);
        subheading.Margin = new Thickness(12, 6, 0, 0);
        titleRow.Children.Add(subheading);
        head.Children.Add(titleRow);
        foreach (var (label, s) in new[] { (L.T("Abiertos"), TaskScope.Open), (L.T("Hoy y atrasados"), TaskScope.Today), (L.T("Próximos"), TaskScope.Upcoming), (L.T("Sin fecha"), TaskScope.NoDate), (L.T("Completados"), TaskScope.Done) })
        {
            var chip = new RadioButton { Content = label, GroupName = "TaskScope", IsChecked = s == scope, Tag = s };
            chip.SetResourceReference(StyleProperty, "SegmentChip");
            chip.Checked += (_, _) => { scope = (TaskScope)chip.Tag; Refresh(); };
            scopes.Children.Add(chip);
        }
        head.Children.Add(scopes);
        add.KeyDown += AddKeyDown;
        add.ToolTip = L.T("Enter para agregar. Entiende: !1 !2 !3 (prioridad), hoy, mañana, viernes, 25/11, 18:30, #etiqueta");
        head.Children.Add(TaskEditor.Placeholder(add, L.T("Agregar un pendiente…  (!3 mañana 9:30 #etiqueta)")));
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
    }

    public override void BuildToolbar(Panel bar)
    {
        bar.Children.Add(Ui.ToolButton("", L.T("Nuevo pendiente"), (_, _) => New(), "Ctrl+N"));
        bar.Children.Add(Ui.ToolButton("", L.T("Nueva área"), (_, _) => Host.NewSection(null)));
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
        if (scope != needed && !(t.IsOpen && scope == TaskScope.Today && t.DueDate <= Clock.Today)) SetScope(needed);
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
        heading.Text = section?.Name ?? L.T("Pendientes");
        long? keep = selectId ?? editor.TaskId;
        selectId = null;
        var items = Tasks.List(new TaskFilter(scope, sectionId, Limit: scope == TaskScope.Done ? 200 : 1000), today);
        subheading.Text = L.Count(items.Count, "pendiente", "pendientes");
        list.Items.Clear();
        foreach (var (label, brush, group) in Group(items, today))
        {
            list.Items.Add(TaskRows.Header(label, group.Count, brush));
            foreach (var t in group) list.Items.Add(new ListBoxItem { Content = TaskRows.Build(t, today, x => editor.Show(x.Id), showSection: sectionId == null), Padding = new Thickness(0) });
        }
        emptyText.Text = items.Count > 0 ? "" : scope switch
        {
            TaskScope.Done => L.T("Todavía no completaste nada acá."),
            TaskScope.Today => L.T("Nada para hoy. Buen momento para planificar la semana."),
            _ => L.T("Sin pendientes. Escribí uno arriba y Enter."),
        };
        emptyText.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
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

    void AddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { add.Text = ""; e.Handled = true; return; }
        if (e.Key != Key.Enter || add.Text.Trim().Length == 0) return;
        var p = QuickParse.Parse(add.Text, Clock.Today);
        if (p.Title.Length == 0) return;
        var due = p.Date ?? (scope == TaskScope.Today ? Clock.Today : null);
        long id = Tasks.Add(new Tasks.Draft(p.Title, sectionId, p.Priority ?? 0, due, p.Time, Tags: p.Tags), Clock.Today);
        add.Text = "";
        bool visible = scope switch
        {
            TaskScope.Today => due <= Clock.Today,
            TaskScope.Upcoming => due > Clock.Today,
            TaskScope.NoDate => due == null,
            TaskScope.Done => false,
            _ => true,
        };
        if (!visible) SetScope(TaskScope.Open);
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
