using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>Home: what is due today or late, today's reminders, topics to review and pinned links, with Boberto.</summary>
public sealed class TodayView : DeskView
{
    public override string Id => "today";
    readonly StackPanel body = new() { Margin = new Thickness(0, 0, 0, 16) };
    readonly ContentControl stageHost = new() { Width = 300, Height = 200, Focusable = false, ToolTip = "Boberto" };
    readonly TextBlock heading = Ui.Title("");
    readonly TextBlock dateLine = Ui.Text("", "Muted", mono: true);
    readonly TextBlock summary = Ui.Text("", "Ink");
    readonly TextBox add = new() { Padding = new Thickness(10, 8, 10, 8) };
    BobertoStageControl? stage;
    int lastOpenToday = -1;

    /// <summary>Blocks other parts of the app add to Today (reminders, topics, links), each rebuilt on refresh.</summary>
    public static readonly List<Func<DateOnly, FrameworkElement?>> Blocks = [];

    public TodayView()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(heading);
        dateLine.Margin = new Thickness(0, 2, 0, 0);
        top.Children.Add(dateLine);
        summary.Margin = new Thickness(0, 10, 0, 12);
        top.Children.Add(summary);
        add.KeyDown += AddKeyDown;
        add.ToolTip = L.T("Enter para agregar. Sin fecha, queda para hoy.");
        top.Children.Add(TaskEditor.Placeholder(add, L.T("Agregar algo para hoy…  (!3 15:00 #etiqueta)")));
        var main = new DockPanel { Margin = new Thickness(8, 4, 8, 0) };
        DockPanel.SetDock(top, Dock.Top);
        main.Children.Add(top);
        main.Children.Add(Ui.Scroll(body));
        Children.Add(main);
        var side = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        side.Children.Add(stageHost);
        SetColumn(side, 1);
        Children.Add(side);
        App.Current.AppearanceChanged += SyncStage;
        Loaded += (_, _) => SyncStage(App.Current.Appearance);
        SizeChanged += (_, _) => side.Visibility = ActualWidth < 760 ? Visibility.Collapsed : Visibility.Visible;
    }

    public override void BuildToolbar(Panel bar)
    {
        bar.Children.Add(Ui.ToolButton("", L.T("Nuevo pendiente"), (_, _) => New(), "Ctrl+N"));
        bar.Children.Add(Ui.ToolButton("", L.T("Planificar mi día"), (_, _) => DayPlanUi.Pick(Window.GetWindow(this)), L.T("Elegí las 3 cosas más importantes de hoy")));
        bar.Children.Add(Ui.ToolButton("", L.T("Ver todos los pendientes"), (_, _) => Host.Navigate("tasks")));
    }

    public override void New() => add.Focus();

    void SyncStage(AppearanceSettings settings)
    {
        if (settings.BobertoVisible && stage == null) { stage = new BobertoStageControl(settings); stageHost.Content = stage; }
        else if (!settings.BobertoVisible && stage != null) { stageHost.Content = null; stage.Dispose(); stage = null; }
        stageHost.Visibility = settings.BobertoVisible ? Visibility.Visible : Visibility.Collapsed;
        if (stage != null) _ = stage.ApplyThemeAsync(settings);
    }

    public override void Shown() { base.Shown(); stage?.Wave(); }
    public void Wave() => stage?.Wave();

    public override void Refresh()
    {
        var now = Clock.Now;
        var today = Clock.Today;
        heading.Text = now.Hour < 12 ? L.T("Buen día") : now.Hour < 20 ? L.T("Buenas tardes") : L.T("Buenas noches");
        dateLine.Text = Fmt.Cap(now.ToString(L.Es ? "dddd d 'de' MMMM 'de' yyyy" : "dddd, MMMM d, yyyy", L.Culture));
        body.Children.Clear();

        var due = Tasks.List(new TaskFilter(TaskScope.Today), today);
        var overdue = due.Where(t => t.DueDate < today).ToList();
        var todays = due.Where(t => t.DueDate == today).ToList();
        var parts = new List<string>();
        if (todays.Count > 0) parts.Add(L.Count(todays.Count, "pendiente para hoy", "pendientes para hoy"));
        if (overdue.Count > 0) parts.Add(L.Count(overdue.Count, "atrasado", "atrasados"));
        summary.Text = parts.Count > 0 ? string.Join(" · ", parts) : L.T("Nada pendiente para hoy.");
        // From the evening on, how the day went.
        if (now.Hour >= 18) summary.Text += "\n" + L.T("Hoy:") + " " + DayPlanUi.Summary(today);
        if (DayPlanUi.TodayBlock(today) is FrameworkElement plan) body.Children.Add(plan);
        // Boberto celebrates when the last task of the day is done.
        if (lastOpenToday > 0 && due.Count == 0) stage?.Wave();
        lastOpenToday = due.Count;

        // Tasks already in the day plan lead the screen; they are not repeated below.
        var planned = DayPlan.Get(today).Select(t => t.Id).ToHashSet();
        var overdueRest = overdue.Where(t => !planned.Contains(t.Id)).ToList();
        var todaysRest = todays.Where(t => !planned.Contains(t.Id)).ToList();
        if (overdueRest.Count > 0) body.Children.Add(TaskBlock(L.T("Atrasados"), "ErrorInk", overdueRest, today));
        if (todaysRest.Count > 0) body.Children.Add(TaskBlock(L.T("Para hoy"), "Accent", todaysRest, today));
        foreach (var block in Blocks)
            if (block(today) is FrameworkElement e) body.Children.Add(e);
        var next = Tasks.List(new TaskFilter(TaskScope.Upcoming, Limit: 5), today);
        if (next.Count > 0) body.Children.Add(TaskBlock(L.T("Lo que viene"), "Muted", next, today));
        if (body.Children.Count == 0)
        {
            var calm = Ui.Text(L.T("Todo en orden. Usá la captura rápida para anotar lo que aparezca."), "Muted");
            calm.Margin = new Thickness(4, 12, 0, 0);
            body.Children.Add(calm);
        }
    }

    FrameworkElement TaskBlock(string title, string brush, List<TaskItem> items, DateOnly today)
    {
        var stack = new StackPanel();
        var label = Ui.Label(title + "  ·  " + items.Count, new Thickness(4, 14, 0, 6));
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        stack.Children.Add(label);
        foreach (var t in items)
        {
            var row = TaskRows.Build(t, today, x => Host.OpenTask(x.Id));
            var frame = new Border { Child = row, Padding = new Thickness(0, 0, 4, 0), Margin = new Thickness(0, 0, 0, 2), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            frame.SetResourceReference(Border.BackgroundProperty, "Surface");
            frame.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
            frame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
            frame.MouseEnter += (_, _) => frame.SetResourceReference(Border.BorderBrushProperty, "StrongBorder");
            frame.MouseLeave += (_, _) => frame.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
            stack.Children.Add(frame);
        }
        return stack;
    }

    void AddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { add.Text = ""; e.Handled = true; return; }
        if (e.Key != Key.Enter || add.Text.Trim().Length == 0) return;
        var p = QuickParse.Parse(add.Text, Clock.Today);
        if (p.Title.Length == 0) return;
        Tasks.Add(new Tasks.Draft(p.Title, null, p.Priority ?? 0, p.Date ?? Clock.Today, p.Time, Tags: p.Tags), Clock.Today);
        add.Text = "";
        e.Handled = true;
    }

    public override void Release() { App.Current.AppearanceChanged -= SyncStage; stage?.Dispose(); stage = null; }
}
