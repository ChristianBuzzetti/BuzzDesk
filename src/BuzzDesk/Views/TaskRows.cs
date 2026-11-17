using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>One task as a list row: round check, priority bar, title, chips (area, tags, repeat, subtasks), due
/// date and a postpone button. Shared by Today and Tasks.</summary>
public static class TaskRows
{
    public static FrameworkElement Build(TaskItem t, DateOnly today, Action<TaskItem> open, bool showSection = true)
    {
        var grid = new Grid { Tag = t, Background = System.Windows.Media.Brushes.Transparent, MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border { CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 6, 0, 6) };
        bar.SetResourceReference(Border.BackgroundProperty, Fmt.PriorityBrush(t.Priority));
        bar.ToolTip = Fmt.Priority(t.Priority);
        grid.Children.Add(bar);

        var check = new CheckBox { IsChecked = !t.IsOpen, Margin = new Thickness(8, 0, 6, 0), ToolTip = t.IsOpen ? L.T("Completar") : L.T("Volver a abrir") };
        check.SetResourceReference(FrameworkElement.StyleProperty, "TaskCheck");
        check.Click += (_, e) =>
        {
            if (t.IsOpen) Actions.Complete(t);
            else Actions.Reopen(t);
            e.Handled = true;
        };
        Grid.SetColumn(check, 1);
        grid.Children.Add(check);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
        var title = Ui.Text(t.Title, t.IsOpen ? "Ink" : "Muted");
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        if (!t.IsOpen) title.TextDecorations = TextDecorations.Strikethrough;
        text.Children.Add(title);
        var meta = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (showSection && t.SectionName != null) meta.Children.Add(Ui.Chip(t.SectionName));
        foreach (var tag in t.Tags) meta.Children.Add(Ui.Chip("#" + tag, "AlternateRow", "Muted"));
        if (t.Recurrence != null) { var g = Ui.Glyph("", 11); g.Margin = new Thickness(0, 0, 8, 0); g.ToolTip = Fmt.Rule(t.Recurrence); meta.Children.Add(g); }
        if (t.SubtaskCount > 0) meta.Children.Add(Small("☐ " + t.SubtaskDone + "/" + t.SubtaskCount));
        if (t.PostponeCount >= 2) meta.Children.Add(Small("↻" + t.PostponeCount, L.F("Pospuesto {0} veces", t.PostponeCount)));
        if (!string.IsNullOrWhiteSpace(t.Notes)) { var g = Ui.Glyph("", 11); g.ToolTip = t.Notes!.Length > 300 ? t.Notes[..300] + "…" : t.Notes; meta.Children.Add(g); }
        if (meta.Children.Count > 0) text.Children.Add(meta);
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        if (t.IsOpen && t.DueDate != null)
        {
            var due = Ui.Text(Fmt.Due(t, today), t.IsOverdue(today) ? "ErrorInk" : t.DueDate == today ? "Accent" : "Muted", 12, mono: true);
            due.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(due, 3);
            grid.Children.Add(due);
        }
        else if (!t.IsOpen && t.CompletedUtc is DateTime done)
        {
            var when = Ui.Text(Fmt.When(done), "Muted", 12, mono: true);
            when.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(when, 3);
            grid.Children.Add(when);
        }

        if (t.IsOpen)
        {
            var later = Ui.IconButton("", (s, _) => Actions.PostponeMenu(t, (FrameworkElement)s!), L.T("Posponer"));
            later.Opacity = 0;
            grid.MouseEnter += (_, _) => later.Opacity = 1;
            grid.MouseLeave += (_, _) => later.Opacity = 0;
            later.GotKeyboardFocus += (_, _) => later.Opacity = 1;
            Grid.SetColumn(later, 4);
            grid.Children.Add(later);
        }

        grid.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not CheckBox) open(t); };
        grid.ContextMenu = Actions.TaskMenu(t);
        return grid;
    }

    /// <summary>A titled group of plain rows (Today, Upcoming): "ATRASADOS · 3" with an optional action on the right,
    /// then one row per task divided by hairlines; the selected one is tinted. <paramref name="empty"/> shows a
    /// muted line when the group has no tasks (otherwise an empty group is left out).</summary>
    public static FrameworkElement Group(string title, string brush, List<TaskItem> items, DateOnly today, Action<TaskItem> open, long? selected,
        FrameworkElement? action = null, string? empty = null, Action? add = null)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (items.Count == 0 && empty == null && add == null) return stack;
        var head = new DockPanel { Margin = new Thickness(2, 16, 0, 4) };
        if (action != null) { DockPanel.SetDock(action, Dock.Right); head.Children.Add(action); }
        var label = Ui.Text(title + (items.Count > 0 ? "   " + items.Count : ""), brush, 13.5, FontWeights.SemiBold);
        head.Children.Add(label);
        stack.Children.Add(head);
        var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 2) };
        rule.SetResourceReference(Border.BackgroundProperty, "FaintBorder");
        stack.Children.Add(rule);
        foreach (var t in items) stack.Children.Add(Plain(t, today, open, selected == t.Id));
        if (items.Count == 0 && empty != null)
        {
            var e = Ui.Text(empty, "Muted", 12.5);
            e.Margin = new Thickness(6, 10, 0, 6);
            stack.Children.Add(e);
        }
        if (add != null)
        {
            var plus = new Button { Content = "+ " + L.T("Agregar pendiente"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
            plus.SetResourceReference(FrameworkElement.StyleProperty, "LinkButton");
            plus.Click += (_, _) => add();
            stack.Children.Add(plus);
        }
        return stack;
    }

    /// <summary>One task as a flat row with a hairline below; hover lifts it, the selected one takes the accent tint.</summary>
    public static Border Plain(TaskItem t, DateOnly today, Action<TaskItem> open, bool isSelected, bool showSection = true)
    {
        var frame = new Border { Child = Build(t, today, open, showSection), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand };
        frame.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
        void Paint(bool hover) { if (isSelected) frame.SetResourceReference(Border.BackgroundProperty, "AccentWeak"); else if (hover) frame.SetResourceReference(Border.BackgroundProperty, "AlternateRow"); else frame.Background = System.Windows.Media.Brushes.Transparent; }
        frame.MouseEnter += (_, _) => Paint(true);
        frame.MouseLeave += (_, _) => Paint(false);
        Paint(false);
        return frame;
    }

    static TextBlock Small(string text, string? tip = null)
    {
        var t = Ui.Text(text, "Muted", 11, mono: true);
        t.Margin = new Thickness(0, 0, 8, 0);
        t.ToolTip = tip;
        return t;
    }

    /// <summary>A non-selectable group header for a ListBox of rows ("ATRASADOS · 3").</summary>
    public static ListBoxItem Header(string text, int count, string brush = "Muted")
    {
        var label = Ui.Label(text + (count > 0 ? "  ·  " + count : ""), new Thickness(4, 14, 0, 4));
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return new ListBoxItem { Content = label, Focusable = false, IsHitTestVisible = false, IsTabStop = false };
    }
}

/// <summary>Task actions shared by the views and menus (complete, postpone, delete with undo).</summary>
public static class Actions
{
    public static void Complete(TaskItem t)
    {
        long? next = Tasks.Complete(t.Id, Clock.Today);
        var main = App.Current.Desk;
        string text = next is long id && Tasks.Get(id)?.DueDate is DateOnly d ? L.F("Hecho: {0}. Próximo: {1}", t.Title, Fmt.Day(d, Clock.Today)) : L.F("Hecho: {0}", t.Title);
        main?.SetStatus(text, L.T("Deshacer"), () => Tasks.UndoComplete(t.Id, next));
    }

    public static void Reopen(TaskItem t) => Tasks.Reopen(t.Id);

    public static void Postpone(TaskItem t, DateOnly to)
    {
        Tasks.Postpone(t.Id, to);
        App.Current.Desk?.SetStatus(L.F("Pospuesto a {0}: {1}", Fmt.Day(to, Clock.Today), t.Title));
    }

    public static void PostponeMenu(TaskItem t, FrameworkElement anchor)
    {
        var menu = new ContextMenu();
        foreach (var item in PostponeItems(t)) menu.Items.Add(item);
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    static List<Control> PostponeItems(TaskItem t)
    {
        var menu = new List<Control>();
        var today = Clock.Today;
        var from = t.DueDate is DateOnly d && d > today ? d : today;
        int toMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        foreach (var (label, date) in new[]
                 {
                     (L.T("Mañana"), today.AddDays(1)),
                     (L.T("Pasado mañana"), today.AddDays(2)),
                     (L.T("El lunes que viene"), today.AddDays(toMonday == 0 ? 7 : toMonday)),
                     (L.T("En una semana"), from.AddDays(7)),
                 })
        {
            var item = new MenuItem { Header = label, InputGestureText = Fmt.Day(date, today) };
            item.Click += (_, _) => Postpone(t, date);
            menu.Add(item);
        }
        menu.Add(new Separator());
        var pick = new MenuItem { Header = L.T("Elegir fecha…") };
        pick.Click += (_, _) => DatePopup.Open(null, t.DueDate, d => { if (d is DateOnly x) Postpone(t, x); }, allowEmpty: false);
        menu.Add(pick);
        return menu;
    }

    public static ContextMenu TaskMenu(TaskItem t)
    {
        var menu = new ContextMenu();
        void Add(string header, Action click, string gesture = "")
        {
            var m = new MenuItem { Header = header, InputGestureText = gesture };
            m.Click += (_, _) => click();
            menu.Items.Add(m);
        }
        if (t.IsOpen) Add(L.T("Completar"), () => Complete(t)); else Add(L.T("Volver a abrir"), () => Reopen(t));
        if (t.IsOpen)
        {
            var later = new MenuItem { Header = L.T("Posponer") };
            foreach (var item in PostponeItems(t)) later.Items.Add(item);
            menu.Items.Add(later);
            var prio = new MenuItem { Header = L.T("Prioridad") };
            for (int p = 3; p >= 0; p--)
            {
                int value = p;
                var m = new MenuItem { Header = Fmt.Priority(p), IsCheckable = true, IsChecked = t.Priority == p };
                m.Click += (_, _) => Tasks.SetPriority(t.Id, value);
                prio.Items.Add(m);
            }
            menu.Items.Add(prio);
        }
        menu.Items.Add(new Separator());
        Add(L.T("Eliminar"), () => Delete(t), "Supr");
        return menu;
    }

    public static void Delete(TaskItem t)
    {
        Tasks.Delete(t.Id);
        App.Current.Desk?.SetStatus(L.F("Eliminado: {0}", t.Title), L.T("Deshacer"), () => Tasks.Restore(t.Id));
    }
}
