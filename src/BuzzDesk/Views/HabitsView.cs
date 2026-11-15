using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>Habits: one touch to check today, the streak, and 12 weeks at a glance.</summary>
public sealed class HabitsView : DeskView
{
    public override string Id => "habits";
    readonly StackPanel list = new();
    readonly TextBox add = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly TextBlock subheading = Ui.Text("", "Muted", 12, mono: true);
    readonly TextBlock emptyText = Ui.Text("", "Muted");

    public HabitsView()
    {
        var root = new DockPanel { Margin = new Thickness(8, 4, 8, 0) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(Ui.Title(L.T("Hábitos")));
        subheading.Margin = new Thickness(12, 6, 0, 0);
        titleRow.Children.Add(subheading);
        head.Children.Add(titleRow);
        add.Margin = new Thickness(0, 10, 0, 0);
        add.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || add.Text.Trim().Length == 0) return;
            Habits.Add(add.Text.Trim());
            add.Text = "";
            e.Handled = true;
        };
        head.Children.Add(TaskEditor.Placeholder(add, L.T("Nuevo hábito…  (Enter) — tomar agua, caminar, leer 20 min")));
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        emptyText.Margin = new Thickness(4, 12, 0, 0);
        DockPanel.SetDock(emptyText, Dock.Top);
        root.Children.Add(emptyText);
        root.Children.Add(Ui.Scroll(list));
        Children.Add(root);
    }

    public override void BuildToolbar(Panel bar) => bar.Children.Add(Ui.ToolButton("", L.T("Nuevo hábito"), (_, _) => New(), "Ctrl+N"));
    public override void New() => add.Focus();

    public override void Refresh()
    {
        var today = Clock.Today;
        var habits = Habits.List(today, includeArchived: true);
        var live = habits.Where(h => !h.Archived).ToList();
        int dueToday = live.Count(h => h.AppliesOn(today)), done = live.Count(h => h.AppliesOn(today) && h.DoneToday);
        subheading.Text = live.Count == 0 ? "" : L.F("{0} de {1} hoy", done, dueToday);
        list.Children.Clear();
        foreach (var h in live) list.Children.Add(Card(h, today));
        var archived = habits.Where(h => h.Archived).ToList();
        if (archived.Count > 0)
        {
            list.Children.Add(Ui.Label(L.T("Archivados") + "  ·  " + archived.Count, new Thickness(4, 20, 0, 6)));
            foreach (var h in archived) list.Children.Add(Card(h, today));
        }
        emptyText.Text = habits.Count == 0 ? L.T("Un hábito es algo que querés hacer seguido: tomar agua, caminar, leer. Marcalo cada día y mirá crecer la racha.") : "";
        emptyText.Visibility = habits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    FrameworkElement Card(Habit h, DateOnly today)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var check = CheckButton(h, today, 44);
        grid.Children.Add(check);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 10, 12, 10) };
        text.Children.Add(Ui.Text(h.Name, h.Archived ? "Muted" : "Ink", 15, FontWeights.SemiBold));
        var meta = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        meta.Children.Add(Ui.Chip("🔥 " + L.Count(h.Streak, "día", "días"), h.Streak > 0 ? "AccentWeak" : "AlternateRow", h.Streak > 0 ? "Accent" : "Muted"));
        if (h.BestStreak > h.Streak) meta.Children.Add(Ui.Chip(L.F("mejor: {0}", h.BestStreak), "AlternateRow", "Muted"));
        if (h.Target > 1) meta.Children.Add(Ui.Chip(L.F("{0} por día", h.Target), "AlternateRow", "Muted"));
        if (h.WeekdaysMask != 127) meta.Children.Add(Ui.Chip(Fmt.Weekdays(h.WeekdaysMask), "AlternateRow", "Muted"));
        text.Children.Add(meta);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var map = HeatMap(h, today);
        map.Margin = new Thickness(0, 8, 12, 8);
        Grid.SetColumn(map, 2);
        grid.Children.Add(map);
        var more = Ui.IconButton("", (s, _) => Menu(h, (FrameworkElement)s!), L.T("Más"));
        Grid.SetColumn(more, 3);
        grid.Children.Add(more);
        var frame = Ui.Panel(grid, new Thickness(10, 4, 8, 4));
        frame.ContextMenu = MenuFor(h);
        return frame;
    }

    /// <summary>A round button: tap adds one (or toggles a once-a-day habit), right click takes one off.</summary>
    public static FrameworkElement CheckButton(Habit h, DateOnly today, double size)
    {
        bool applies = h.AppliesOn(today);
        var ring = new System.Windows.Shapes.Ellipse { Width = size, Height = size, StrokeThickness = 2 };
        ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, h.DoneToday ? "Accent" : "StrongBorder");
        ring.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, h.DoneToday ? "Accent" : h.TodayCount > 0 ? "AccentWeak" : "Surface");
        var label = new TextBlock
        {
            Text = h.DoneToday && h.Target == 1 ? "" : h.Target > 1 ? h.TodayCount + "/" + h.Target : "",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold,
            FontSize = h.Target > 1 ? size * 0.28 : size * 0.4,
            FontFamily = h.Target > 1 ? null : new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        };
        if (h.Target > 1) label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        label.SetResourceReference(TextBlock.ForegroundProperty, h.DoneToday ? "OnAccent" : "Ink");
        var grid = new Grid { Width = size, Height = size, Cursor = Cursors.Hand, Opacity = applies || h.TodayCount > 0 ? 1 : 0.45, IsEnabled = !h.Archived,
            ToolTip = applies ? (h.Target == 1 ? L.T("Marcar hoy · clic derecho deshace") : L.T("Sumar uno · clic derecho resta")) : L.T("Hoy no toca (igual podés marcarlo)") };
        grid.Children.Add(ring); grid.Children.Add(label);
        grid.MouseLeftButtonUp += (_, e) =>
        {
            if (h.Target == 1 && h.DoneToday) Habits.Mark(h.Id, today, -1);
            else
            {
                int n = Habits.Mark(h.Id, today, 1);
                if (n == h.Target && Habits.List(today).FirstOrDefault(x => x.Id == h.Id) is { } now)
                    App.Current.Desk?.Celebrate(L.F("¡{0}! Racha: {1}", h.Name, L.Count(now.Streak, "día", "días")));
            }
            e.Handled = true;
        };
        grid.MouseRightButtonUp += (_, e) => { if (h.TodayCount > 0) Habits.Mark(h.Id, today, -1); e.Handled = true; };
        return grid;
    }

    /// <summary>12 weeks × 7 days (Monday on top), today in the last column.</summary>
    static FrameworkElement HeatMap(Habit h, DateOnly today)
    {
        const int weeks = 12, cell = 11, gap = 2;
        var lastMonday = RecurrenceRule.WeekStart(today);
        var first = lastMonday.AddDays(-7 * (weeks - 1));
        var counts = Habits.History(h.Id, first, today);
        var canvas = new Canvas { Width = weeks * (cell + gap), Height = 7 * (cell + gap), ToolTip = L.T("Últimas 12 semanas") };
        for (int w = 0; w < weeks; w++)
            for (int d = 0; d < 7; d++)
            {
                var day = first.AddDays(w * 7 + d);
                if (day > today) continue;
                int n = counts.GetValueOrDefault(day);
                var r = new System.Windows.Shapes.Rectangle { Width = cell, Height = cell, RadiusX = 2, RadiusY = 2, ToolTip = Fmt.Cap(day.ToString("ddd d/M", L.Culture)) + (n > 0 ? "  ·  " + n : "") };
                string brush = n >= h.Target ? "Accent" : n > 0 ? "AccentRule" : h.AppliesOn(day) ? "AlternateRow" : "Surface";
                r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
                if (day == today) { r.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink"); r.StrokeThickness = 1; }
                Canvas.SetLeft(r, w * (cell + gap)); Canvas.SetTop(r, d * (cell + gap));
                canvas.Children.Add(r);
            }
        return canvas;
    }

    void Menu(Habit h, FrameworkElement anchor) { var m = MenuFor(h); m.PlacementTarget = anchor; m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; m.IsOpen = true; }

    ContextMenu MenuFor(Habit h)
    {
        var menu = new ContextMenu();
        void Add(string header, Action a) { var m = new MenuItem { Header = header }; m.Click += (_, _) => a(); menu.Items.Add(m); }
        Add(L.T("Editar…"), () => Edit(h));
        Add(L.T("Marcar ayer"), () => Habits.Mark(h.Id, Clock.Today.AddDays(-1)));
        menu.Items.Add(new Separator());
        Add(h.Archived ? L.T("Reactivar") : L.T("Archivar"), () => Habits.Archive(h.Id, !h.Archived));
        Add(L.T("Eliminar con su historial…"), () =>
        {
            if (PromptWindow.Confirm(Window.GetWindow(this), L.T("Eliminar hábito"), L.F("¿Eliminar \"{0}\" y todo su historial? Archivar lo conserva.", h.Name), L.T("Eliminar"), L.T("Cancelar")))
                Habits.Delete(h.Id);
        });
        return menu;
    }

    void Edit(Habit h)
    {
        var form = new FormWindow(L.T("Editar hábito"));
        var name = form.Add(L.T("Nombre"), new TextBox { Text = h.Name });
        var target = form.Add(L.T("Veces por día"), new TextBox { Text = h.Target.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left }, L.T("Por ejemplo 8 vasos de agua. 1 = una vez."));
        int mask = h.WeekdaysMask;
        form.Add(L.T("Días"), FormWindow.WeekdayPicker(mask, m => mask = m), L.T("Los días que no toca no cortan la racha."));
        if (!form.Ask(Window.GetWindow(this), () =>
            name.Text.Trim().Length == 0 ? L.T("Poné un nombre.") : !int.TryParse(target.Text, out int t) || t is < 1 or > 50 ? L.T("Veces por día: de 1 a 50.") : mask == 0 ? L.T("Elegí al menos un día.") : null)) return;
        Habits.Update(h.Id, name.Text, int.Parse(target.Text), mask, h.Icon, h.Color);
    }

    /// <summary>Today's block: the habits that apply today, as round checks.</summary>
    public static FrameworkElement? TodayBlock(DateOnly today)
    {
        var habits = Habits.List(today).Where(h => h.AppliesOn(today)).ToList();
        if (habits.Count == 0) return null;
        var stack = new StackPanel();
        int done = habits.Count(h => h.DoneToday);
        stack.Children.Add(Ui.Label(L.T("Hábitos") + "  ·  " + done + "/" + habits.Count, new Thickness(4, 14, 0, 6)));
        var wrap = new WrapPanel();
        foreach (var h in habits)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 8) };
            item.Children.Add(CheckButton(h, today, 32));
            var t = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(Ui.Text(h.Name, "Ink"));
            if (h.Streak > 0) t.Children.Add(Ui.Text("🔥 " + h.Streak, "Accent", 11, mono: true));
            item.Children.Add(t);
            wrap.Children.Add(item);
        }
        stack.Children.Add(wrap);
        return stack;
    }
}
