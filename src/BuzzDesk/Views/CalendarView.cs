using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>
/// Calendar, as in TickTick or Todoist: a month or a week with the tasks on their day, plus bills and important
/// dates. Drag a task to another day to move it; double-click a day (or its "+") to add a task there; click a task
/// to edit it in the panel on the right.
/// </summary>
public sealed class CalendarView : DeskView
{
    public override string Id => "calendar";
    const string DragFormat = "BuzzDesk.TaskId";
    readonly TextBlock heading = Ui.Title("");
    readonly Grid surface = new();
    readonly TaskEditor editor = new();
    readonly ColumnDefinition editorColumn = new() { Width = new GridLength(380) };
    readonly RadioButton monthChip = new(), weekChip = new();
    DateOnly anchor = Clock.Today;
    bool weekMode;
    long? selected;

    public CalendarView()
    {
        weekMode = Store.Settings.CalendarMode == "week";
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(editorColumn);
        var left = new DockPanel { Margin = new Thickness(8, 4, 14, 0) };

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var modes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (chip, label, week) in new[] { (monthChip, L.T("Mes"), false), (weekChip, L.T("Semana"), true) })
        {
            chip.Content = label; chip.GroupName = "CalendarMode"; chip.IsChecked = week == weekMode; chip.Margin = new Thickness(0, 0, 6, 0);
            chip.SetResourceReference(StyleProperty, "SegmentChip");
            chip.Checked += (_, _) => { weekMode = week; Store.Settings.CalendarMode = week ? "week" : "month"; Store.SaveSettings(); Refresh(); };
            modes.Children.Add(chip);
        }
        DockPanel.SetDock(modes, Dock.Right);
        head.Children.Add(modes);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(Ui.IconButton("", (_, _) => Move(-1), L.T("Anterior")));
        var todayButton = new Button { Content = L.T("Hoy"), Margin = new Thickness(4, 0, 4, 0), MinHeight = 30 };
        todayButton.Click += (_, _) => { anchor = Clock.Today; Refresh(); };
        nav.Children.Add(todayButton);
        nav.Children.Add(Ui.IconButton("", (_, _) => Move(1), L.T("Siguiente")));
        heading.Margin = new Thickness(14, 0, 0, 0);
        nav.Children.Add(heading);
        head.Children.Add(nav);
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);

        var hint = Ui.Text(L.T("Arrastrá un pendiente a otro día para moverlo · doble clic en un día para agregar"), "LineNumber", 11.5);
        hint.Margin = new Thickness(2, 8, 0, 0);
        DockPanel.SetDock(hint, Dock.Bottom);
        left.Children.Add(hint);
        left.Children.Add(surface);
        Children.Add(left);
        SetColumn(editor, 1);
        Children.Add(editor);
        editorColumn.Width = new GridLength(0);
        editor.Visibility = Visibility.Collapsed;
        PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers == ModifierKeys.Control) { Move(e.Delta > 0 ? -1 : 1); e.Handled = true; } };
    }

    public override void BuildToolbar(Panel bar)
    {
        bar.Children.Add(Ui.ToolButton("", L.T("Nuevo pendiente"), (_, _) => New(), "Ctrl+N"));
        bar.Children.Add(Ui.ToolButton("", L.T("Cerrar el panel"), (_, _) => CloseEditor(), L.T("Oculta el detalle del pendiente")));
    }

    public override void New() => AddOn(selected is long id && Tasks.Get(id)?.DueDate is DateOnly d ? d : anchor);

    void Move(int step) { anchor = weekMode ? anchor.AddDays(7 * step) : anchor.AddMonths(step); Refresh(); }

    /// <summary>Shows a task in the panel, moving to its day.</summary>
    public void Select(long id)
    {
        if (Tasks.Get(id) is { DueDate: DateOnly d }) anchor = d;
        OpenEditor(id);
    }

    void OpenEditor(long id)
    {
        selected = id;
        editor.Show(id);
        editor.Visibility = Visibility.Visible;
        editorColumn.Width = new GridLength(380);
        Refresh();
    }

    /// <summary>Screenshots: the week view without saving the choice.</summary>
    internal void ShotWeek() { weekMode = true; weekChip.IsChecked = true; Refresh(); }

    void CloseEditor() { selected = null; editor.Visibility = Visibility.Collapsed; editorColumn.Width = new GridLength(0); Refresh(); }

    public override void Refresh()
    {
        var today = Clock.Today;
        surface.Children.Clear(); surface.RowDefinitions.Clear(); surface.ColumnDefinitions.Clear();
        DateOnly first, last;
        if (weekMode)
        {
            first = WeekStart(anchor); last = first.AddDays(6);
            heading.Text = first.Month == last.Month
                ? first.Day + "–" + last.Day + " " + last.ToString("MMMM yyyy", L.Culture)
                : first.ToString("d MMM", L.Culture) + " – " + last.ToString("d MMM yyyy", L.Culture);
        }
        else
        {
            var month = new DateOnly(anchor.Year, anchor.Month, 1);
            first = MiniCalendar.GridStart(month); last = first.AddDays(41);
            heading.Text = Fmt.Cap(month.ToString("MMMM yyyy", L.Culture));
        }
        var items = Items(first, last, today);

        for (int c = 0; c < 7; c++) surface.ColumnDefinitions.Add(new ColumnDefinition());
        surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int col = 0;
        foreach (var name in Enumerable.Range(0, 7).Select(i => Fmt.Cap(L.Culture.DateTimeFormat.AbbreviatedDayNames[((int)MiniCalendar.FirstDay + i) % 7])))
        {
            var t = Ui.Text(name, "Muted", 12, FontWeights.SemiBold);
            t.Margin = new Thickness(8, 0, 0, 6);
            Grid.SetColumn(t, col++);
            surface.Children.Add(t);
        }
        int weeks = weekMode ? 1 : 6;
        for (int r = 0; r < weeks; r++) surface.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < weeks * 7; i++)
        {
            var day = first.AddDays(i);
            var cell = Cell(day, today, items.TryGetValue(day, out var list) ? list : [], weekMode || day.Month == anchor.Month);
            Grid.SetRow(cell, 1 + i / 7); Grid.SetColumn(cell, i % 7);
            surface.Children.Add(cell);
        }
    }

    DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek - (int)MiniCalendar.FirstDay + 7) % 7));

    // ------------------------------------------------------------------ what goes on each day
    sealed record Item(string Kind, string Text, string Brush, TaskItem? Task = null, string? Tip = null);

    static Dictionary<DateOnly, List<Item>> Items(DateOnly from, DateOnly to, DateOnly today)
    {
        var map = new Dictionary<DateOnly, List<Item>>();
        void Put(DateOnly d, Item i) { if (!map.TryGetValue(d, out var l)) map[d] = l = []; l.Add(i); }
        foreach (var t in Tasks.Between(from, to))
            Put(t.DueDate!.Value, new Item("task", (t.DueTime is TimeOnly time ? Fmt.Time(time) + " " : "") + t.Title, t.IsOpen ? (t.IsOverdue(today) ? "ErrorInk" : "Ink") : "Muted", t,
                t.Title + (t.SectionName != null ? "\n" + t.SectionName : "") + (t.Notes is { Length: > 0 } n ? "\n\n" + (n.Length > 200 ? n[..200] + "…" : n) : "")));
        foreach (var b in Bills.List())
        {
            if (b.NextDue is not DateOnly due) continue;
            // The next due date, and the following ones of its rule that fall in view.
            for (int guard = 0; guard < 60 && due <= to; guard++)
            {
                if (due >= from) Put(due, new Item("bill", (b.IsRenewal ? "📄 " : "💳 ") + b.Name, "SqlNumber", Tip: b.Name + (b.AmountCents is long a ? " · " + (a / 100m).ToString("N2", L.Culture) + " " + b.Currency : "")));
                if (b.Recurrence?.Next(due) is not DateOnly next || next <= due) break;
                due = next;
            }
        }
        foreach (var d in Dates.All())
        {
            var on = d.Next(from);
            if (on <= to) Put(on, new Item("date", (d.Kind == "birthday" ? "🎂 " : d.Kind == "anniversary" ? "💍 " : "📅 ") + d.Label, "Accent", Tip: d.Label));
        }
        return map;
    }

    // ------------------------------------------------------------------ cells
    Border Cell(DateOnly day, DateOnly today, List<Item> items, bool inRange)
    {
        bool isToday = day == today;
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(6, 3, 4, 2) };
        var plus = Ui.IconButton("", (_, _) => AddOn(day), L.T("Agregar un pendiente este día"));
        plus.Width = plus.Height = 22; plus.FontSize = 10; plus.Opacity = 0;
        DockPanel.SetDock(plus, Dock.Right);
        head.Children.Add(plus);
        var number = new TextBlock { Text = weekMode ? Fmt.Cap(day.ToString("d MMM", L.Culture)) : day.Day == 1 ? Fmt.Cap(day.ToString("d MMM", L.Culture)) : day.Day.ToString(), FontSize = 12.5, FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, isToday ? "OnAccent" : inRange ? (day < today ? "Muted" : "Ink") : "LineNumber");
        var badge = new Border { Child = number, Padding = new Thickness(7, 0, 7, 1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left };
        if (isToday) badge.SetResourceReference(Border.BackgroundProperty, "Accent");
        head.Children.Add(badge);
        stack.Children.Add(head);

        int max = weekMode ? int.MaxValue : 3;
        foreach (var item in items.Take(items.Count > max ? max - 1 : max)) stack.Children.Add(Chip(item));
        if (items.Count > max)
        {
            var more = new Button { Content = L.F("+{0} más", items.Count - (max - 1)), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0), FontSize = 11.5 };
            more.SetResourceReference(StyleProperty, "LinkButton");
            more.Click += (_, _) => { anchor = day; weekChip.IsChecked = true; };
            stack.Children.Add(more);
        }

        FrameworkElement content = weekMode ? Ui.Scroll(stack) : stack;
        var cell = new Border { Child = content, BorderThickness = new Thickness(0, 0, 1, 1), MinHeight = weekMode ? 300 : 96, AllowDrop = true, Background = Brushes.Transparent, ClipToBounds = true };
        cell.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
        if (!inRange) cell.SetResourceReference(Border.BackgroundProperty, "Header");
        else if (isToday) cell.SetResourceReference(Border.BackgroundProperty, "AccentTint");
        cell.MouseEnter += (_, _) => plus.Opacity = 1;
        cell.MouseLeave += (_, _) => plus.Opacity = 0;
        cell.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { AddOn(day); e.Handled = true; } };
        void Hover(bool on) { if (on) cell.SetResourceReference(Border.BackgroundProperty, "AccentWeak"); else if (!inRange) cell.SetResourceReference(Border.BackgroundProperty, "Header"); else if (isToday) cell.SetResourceReference(Border.BackgroundProperty, "AccentTint"); else cell.Background = Brushes.Transparent; }
        cell.DragEnter += (_, e) => { if (e.Data.GetDataPresent(DragFormat)) Hover(true); };
        cell.DragLeave += (_, _) => Hover(false);
        cell.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        cell.Drop += (_, e) =>
        {
            Hover(false);
            if (e.Data.GetData(DragFormat) is not long id || Tasks.Get(id) is not { } t || t.DueDate == day) return;
            Tasks.Reschedule(id, day);
            Host.SetStatus(L.F("Movido a {0}: {1}", Fmt.Day(day, Clock.Today), t.Title), L.T("Deshacer"), () => { if (t.DueDate is DateOnly back) Tasks.Reschedule(id, back); });
        };
        return cell;
    }

    FrameworkElement Chip(Item item)
    {
        var row = new DockPanel();
        if (item.Task is TaskItem t)
        {
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1.2) };
            dot.SetResourceReference(Border.BorderBrushProperty, t.Priority > 0 ? Fmt.PriorityBrush(t.Priority) : "StrongBorder");
            if (!t.IsOpen) dot.SetResourceReference(Border.BackgroundProperty, "Muted");
            row.Children.Add(dot);
        }
        var text = Ui.Text(item.Text, item.Brush, 12);
        text.TextWrapping = weekMode ? TextWrapping.Wrap : TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        if (item.Task is { IsOpen: false }) text.TextDecorations = TextDecorations.Strikethrough;
        row.Children.Add(text);
        var chip = new Border { Child = row, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(4, 0, 4, 1), CornerRadius = new CornerRadius(5), ToolTip = item.Tip, Background = Brushes.Transparent };
        bool isSel = item.Task?.Id == selected && selected != null;
        if (isSel) chip.SetResourceReference(Border.BackgroundProperty, "AccentWeak");
        else if (item.Kind != "task") chip.SetResourceReference(Border.BackgroundProperty, "AlternateRow");
        if (item.Task is not TaskItem task) return chip;

        chip.Cursor = Cursors.Hand;
        chip.MouseEnter += (_, _) => { if (!isSel) chip.SetResourceReference(Border.BackgroundProperty, "AlternateRow"); };
        chip.MouseLeave += (_, _) => { if (!isSel) chip.Background = Brushes.Transparent; };
        chip.ContextMenu = Actions.TaskMenu(task);
        Point? down = null;
        chip.MouseLeftButtonDown += (_, e) => { down = e.GetPosition(chip); if (e.ClickCount == 1) e.Handled = true; };
        chip.MouseLeftButtonUp += (_, e) => { if (down != null) { down = null; OpenEditor(task.Id); e.Handled = true; } };
        chip.MouseMove += (_, e) =>
        {
            if (down is not Point start || e.LeftButton != MouseButtonState.Pressed || !task.IsOpen) return;
            var p = e.GetPosition(chip);
            if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            chip.Opacity = 0.5;
            DragDrop.DoDragDrop(chip, new DataObject(DragFormat, task.Id), DragDropEffects.Move);
            chip.Opacity = 1;
        };
        return chip;
    }

    /// <summary>Adds a task on a day: one line, read like quick capture ("llamar a Juan 15:00 !2 #trabajo").</summary>
    void AddOn(DateOnly day)
    {
        string? text = PromptWindow.Ask(Window.GetWindow(this), L.T("Nuevo pendiente"),
            L.F("Para el {0}. Podés agregar hora, !1-!3 y #etiquetas.", Fmt.Cap(day.ToString("dddd d MMMM", L.Culture))), "");
        if (string.IsNullOrWhiteSpace(text)) return;
        var p = QuickParse.Parse(text, Clock.Today);
        if (p.Title.Length == 0) return;
        long id = Tasks.Add(new Tasks.Draft(p.Title, null, p.Priority ?? 0, p.Date ?? day, p.Time, Tags: p.Tags), Clock.Today);
        anchor = p.Date ?? day;
        OpenEditor(id);
    }
}
