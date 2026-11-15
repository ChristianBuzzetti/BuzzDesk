using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;

namespace BuzzDesk;

/// <summary>"Plan my day": pick up to three tasks that matter most today; they lead Today, with a short summary of
/// how the day went.</summary>
public static class DayPlanUi
{
    /// <summary>The picker: overdue, today's, the next days' and undated tasks (highest priority first); the order
    /// of the ticks is the order of the plan.</summary>
    public static void Pick(Window? owner)
    {
        var today = Clock.Today;
        var current = DayPlan.Get(today).Select(t => t.Id).ToList();
        var open = Tasks.List(new TaskFilter(TaskScope.Open, Limit: 300), today);
        var candidates = open.Where(t => t.DueDate <= today).Concat(open.Where(t => t.DueDate > today && t.DueDate <= today.AddDays(7)))
                             .Concat(open.Where(t => t.DueDate == null).OrderByDescending(t => t.Priority)).Concat(open)
                             .DistinctBy(t => t.Id).Take(40).ToList();
        var chosen = new List<long>(current.Where(id => candidates.Any(c => c.Id == id)));
        var form = new FormWindow(L.T("Planificar mi día"));
        var hint = Ui.Text(L.T("Elegí hasta 3 cosas que harían que hoy sea un buen día. El orden en que las marcás es su orden."), "Muted");
        hint.Margin = new Thickness(0, 10, 0, 6);
        form.AddRaw(hint);
        var counter = Ui.Text("", "Accent", 12, mono: true);
        form.AddRaw(counter);
        var rows = new StackPanel();
        var checks = new List<(CheckBox box, TextBlock order, long id)>();
        void Sync()
        {
            counter.Text = L.F("{0} de 3", chosen.Count);
            foreach (var (box, order, id) in checks)
            {
                int i = chosen.IndexOf(id);
                box.IsChecked = i >= 0;
                order.Text = i >= 0 ? (i + 1).ToString() : "";
                box.IsEnabled = i >= 0 || chosen.Count < 3;
            }
        }
        foreach (var t in candidates)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
            var order = Ui.Text("", "Accent", 13, FontWeights.Bold, mono: true);
            order.Width = 18;
            var box = new CheckBox { Margin = new Thickness(4, 0, 10, 0) };
            box.SetResourceReference(FrameworkElement.StyleProperty, "TaskCheck");
            long id = t.Id;
            void Toggle() { if (chosen.Contains(id)) chosen.Remove(id); else if (chosen.Count < 3) chosen.Add(id); Sync(); }
            box.Click += (_, e) => { Toggle(); e.Handled = true; };
            row.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not CheckBox) Toggle(); };
            DockPanel.SetDock(order, Dock.Left); DockPanel.SetDock(box, Dock.Left);
            row.Children.Add(order); row.Children.Add(box);
            var due = Ui.Text(Fmt.Due(t, today), t.IsOverdue(today) ? "ErrorInk" : "Muted", 11.5, mono: true);
            DockPanel.SetDock(due, Dock.Right);
            row.Children.Add(due);
            var title = Ui.Text(t.Title, "Ink");
            title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap;
            row.Children.Add(title);
            rows.Children.Add(row);
            checks.Add((box, order, id));
        }
        if (candidates.Count == 0) rows.Children.Add(Ui.Text(L.T("No hay pendientes abiertos. Agregá alguno y volvé a planificar."), "Muted"));
        form.AddRaw(new ScrollViewer { Content = rows, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        Sync();
        if (!form.Ask(owner, () => null)) return;
        DayPlan.Set(today, chosen);
        App.Current.Desk?.SetStatus(chosen.Count == 0 ? L.T("Plan del día vacío.") : L.F("Plan del día: {0}", string.Join(" · ", chosen.Select(id => candidates.First(c => c.Id == id).Title))));
    }

    /// <summary>Today's lead block: the plan with its progress, or a nudge to make one.</summary>
    public static FrameworkElement? TodayBlock(DateOnly today)
    {
        var plan = DayPlan.Get(today);
        var stack = new StackPanel();
        if (plan.Count == 0)
        {
            var b = Ui.ToolButton("", L.T("Planificar mi día"), (_, _) => Pick(App.Current.Desk), L.T("Elegí las 3 cosas más importantes de hoy"));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(b);
            return stack;
        }
        int done = plan.Count(t => !t.IsOpen);
        var head = new DockPanel { Margin = new Thickness(4, 14, 0, 6) };
        var edit = Ui.IconButton("", (_, _) => Pick(App.Current.Desk), L.T("Cambiar el plan"));
        DockPanel.SetDock(edit, Dock.Right);
        head.Children.Add(edit);
        var label = Ui.Label(L.T("Tus prioridades de hoy") + "  ·  " + done + "/" + plan.Count, new Thickness(0, 0, 0, 0));
        label.SetResourceReference(TextBlock.ForegroundProperty, done == plan.Count ? "Ok" : "Accent");
        head.Children.Add(label);
        stack.Children.Add(head);
        int n = 1;
        foreach (var t in plan)
        {
            var row = new DockPanel();
            var num = Ui.Text((n++).ToString(), "Accent", 16, FontWeights.Bold, mono: true);
            num.Width = 22; num.Margin = new Thickness(4, 0, 4, 0);
            DockPanel.SetDock(num, Dock.Left);
            row.Children.Add(num);
            row.Children.Add(TaskRows.Build(t, today, x => App.Current.Desk?.OpenTask(x.Id)));
            var frame = new Border { Child = row, Margin = new Thickness(0, 0, 0, 2), BorderThickness = new Thickness(1) };
            frame.SetResourceReference(Border.BackgroundProperty, "Surface");
            frame.SetResourceReference(Border.BorderBrushProperty, t.IsOpen ? "AccentCardRing" : "FaintBorder");
            frame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
            stack.Children.Add(frame);
        }
        if (done == plan.Count) { var ok = Ui.Text(L.T("¡Prioridades del día cumplidas!"), "Ok", 12.5); ok.Margin = new Thickness(6, 6, 0, 0); stack.Children.Add(ok); }
        return stack;
    }

    /// <summary>"How did today go": priorities, tasks done today, habits.</summary>
    public static string Summary(DateOnly today)
    {
        var plan = DayPlan.Get(today);
        var start = Clock.ToUtc(today, TimeOnly.MinValue);
        int doneToday = Tasks.List(new TaskFilter(TaskScope.Done, Limit: 500), today).Count(t => t.CompletedUtc >= start);
        var habits = Data.Habits.List(today).Where(h => h.AppliesOn(today)).ToList();
        var parts = new List<string>();
        if (plan.Count > 0) parts.Add(L.F("{0} de {1} prioridades", plan.Count(t => !t.IsOpen), plan.Count));
        parts.Add(L.Count(doneToday, "pendiente hecho", "pendientes hechos"));
        if (habits.Count > 0) parts.Add(L.F("hábitos {0}/{1}", habits.Count(h => h.DoneToday), habits.Count));
        return string.Join("  ·  ", parts);
    }
}
