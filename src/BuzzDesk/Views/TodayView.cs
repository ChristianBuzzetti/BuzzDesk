using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>Today, as in Todoist or TickTick: overdue (with "move all to today"), today's tasks, then the day's
/// habits, reminders, bills and dates. Clicking a task opens it in the panel on the right.</summary>
public sealed class TodayView : DeskView
{
    public override string Id => "today";
    readonly StackPanel body = new() { Margin = new Thickness(0, 0, 0, 16) };
    readonly TextBlock heading = Ui.Title(L.T("Hoy"));
    readonly TextBlock dateLine = Ui.Text("", "Muted", 13);
    readonly TextBox add = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly TaskEditor editor = new();
    readonly ColumnDefinition editorColumn = new() { Width = new GridLength(400) };
    long? selected;

    /// <summary>Blocks other parts of the app add to Today (habits, reminders, bills, dates…), rebuilt on refresh.</summary>
    public static readonly List<Func<DateOnly, FrameworkElement?>> Blocks = [];

    public TodayView()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(editorColumn);
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var titleRow = new WrapPanel();
        titleRow.Children.Add(heading);
        dateLine.Margin = new Thickness(12, 8, 0, 0);
        titleRow.Children.Add(dateLine);
        top.Children.Add(titleRow);
        add.KeyDown += AddKeyDown;
        add.ToolTip = L.T("Enter para agregar. Sin fecha, queda para hoy.");
        var box = TaskEditor.Placeholder(add, L.T("+ Agregar pendiente para hoy   (ej.: llamar al banco 15:00 !3 #trámites)"));
        box.Margin = new Thickness(0, 14, 0, 0);
        top.Children.Add(box);
        var main = new DockPanel { Margin = new Thickness(8, 4, 16, 0) };
        DockPanel.SetDock(top, Dock.Top);
        main.Children.Add(top);
        main.Children.Add(Ui.Scroll(body));
        Children.Add(main);
        SetColumn(editor, 1);
        Children.Add(editor);
        // A narrow window gives the list the whole width; the editor comes back when there is room.
        SizeChanged += (_, _) => { bool room = ActualWidth >= 860; editor.Visibility = room ? Visibility.Visible : Visibility.Collapsed; editorColumn.Width = new GridLength(room ? 400 : 0); };
    }

    public override void BuildToolbar(Panel bar)
    {
        bar.Children.Add(Ui.ToolButton("", L.T("Nuevo pendiente"), (_, _) => New(), "Ctrl+N"));
        bar.Children.Add(Ui.ToolButton("", L.T("Planificar mi día"), (_, _) => DayPlanUi.Pick(Window.GetWindow(this)), L.T("Elegí las 3 cosas más importantes de hoy")));
    }

    public override void New() => add.Focus();

    public void Wave() { }

    public override void Refresh()
    {
        var now = Clock.Now;
        var today = Clock.Today;
        dateLine.Text = Fmt.Cap(now.ToString(L.Es ? "dddd d 'de' MMMM" : "dddd, MMMM d", L.Culture));
        body.Children.Clear();

        var due = Tasks.List(new TaskFilter(TaskScope.Today), today);
        var overdue = due.Where(t => t.DueDate < today).ToList();
        var todays = due.Where(t => t.DueDate == today).ToList();

        if (DayPlan.Get(today).Count > 0 && DayPlanUi.TodayBlock(today) is FrameworkElement plan) body.Children.Add(plan);
        var planned = DayPlan.Get(today).Select(t => t.Id).ToHashSet();
        var overdueRest = overdue.Where(t => !planned.Contains(t.Id)).ToList();
        var todaysRest = todays.Where(t => !planned.Contains(t.Id)).ToList();
        if (overdueRest.Count > 0)
        {
            var move = new Button { Content = L.T("Reprogramar a hoy"), ToolTip = L.T("Pasa todos los atrasados a hoy") };
            move.SetResourceReference(StyleProperty, "LinkButton");
            move.Click += (_, _) => { foreach (var t in overdueRest) Tasks.Reschedule(t.Id, today); Host.SetStatus(L.Count(overdueRest.Count, "pendiente movido a hoy", "pendientes movidos a hoy")); };
            body.Children.Add(TaskRows.Group(L.T("Atrasados"), "ErrorInk", overdueRest, today, Open, selected, move));
        }
        body.Children.Add(TaskRows.Group(Fmt.Cap(today.ToString(L.Es ? "d MMM" : "MMM d", L.Culture)) + " · " + L.T("Hoy"), "Accent", todaysRest, today, Open, selected,
            empty: todaysRest.Count == 0 && overdueRest.Count == 0 && planned.Count == 0 ? L.T("Nada pendiente para hoy. ¡Disfrutá el día!") : null));
        foreach (var block in Blocks)
            if (block(today) is FrameworkElement e) body.Children.Add(e);
        if (selected is long id) editor.Show(id);
    }

    void Open(TaskItem t) { selected = t.Id; editor.Show(t.Id); Refresh(); }

    /// <summary>Shows a task in the panel (from the calendar or a reminder).</summary>
    public void Select(long id) { selected = id; Refresh(); }

    void AddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { add.Text = ""; e.Handled = true; return; }
        if (e.Key != Key.Enter || add.Text.Trim().Length == 0) return;
        var p = QuickParse.Parse(add.Text, Clock.Today);
        if (p.Title.Length == 0) return;
        selected = Tasks.Add(new Tasks.Draft(p.Title, null, p.Priority ?? 0, p.Date ?? Clock.Today, p.Time, Tags: p.Tags), Clock.Today);
        add.Text = "";
        e.Handled = true;
    }
}
