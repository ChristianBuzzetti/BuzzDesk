using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>Reminder pieces for the editors (a task's or a topic's reminders) and for Today.</summary>
public static class ReminderUi
{
    /// <summary>"RECORDATORIOS" block: the pending ones with a remove button, quick presets and a free text box
    /// ("mañana 15:00", "viernes 9", "18hs"). <paramref name="due"/> gives the owner's due date for "on the due date".</summary>
    public static StackPanel EditorBlock(Func<List<Reminder>> load, Func<DateTime, long> add, Func<(DateOnly? date, TimeOnly? time)>? due = null)
    {
        var block = new StackPanel();
        var list = new StackPanel();
        var hint = Ui.Text("", "Muted", 11.5);
        var box = new TextBox { MinWidth = 160 };

        void Reload()
        {
            list.Children.Clear();
            foreach (var r in load())
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = System.Windows.Media.Brushes.Transparent };
                var bell = Ui.Glyph(r.FiredUtc != null ? "" : "", 12, r.FiredUtc != null ? "Accent" : "Muted");
                bell.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(bell, Dock.Left);
                row.Children.Add(bell);
                var remove = Ui.IconButton("", (_, _) => { Reminders.Delete(r.Id); Toasts.Remove(r.Id); Reload(); }, L.T("Quitar recordatorio"));
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                string text = Fmt.When(r.DueUtc) + (r.SnoozedUntilUtc != null ? "  ·  " + L.T("pospuesto") : r.FiredUtc != null ? "  ·  " + L.T("sonó") : "");
                row.Children.Add(Ui.Text(text, "Ink", mono: true));
                list.Children.Add(row);
            }
        }

        void Add(DateTime utc)
        {
            if (utc <= DateTime.UtcNow.AddSeconds(30)) { Hint(hint, L.T("Esa hora ya pasó."), true); return; }
            add(utc);
            Hint(hint, L.F("Suena {0}.", Fmt.When(utc)), false);
            box.Text = "";
            Reload();
            App.Current.OfferStartup(Window.GetWindow(block));
        }

        block.Children.Add(Ui.Label(L.T("Recordatorios")));
        block.Children.Add(list);
        var presets = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        void Preset(string label, Func<DateTime?> when, string? tip = null)
        {
            var b = new Button { Content = label, Padding = new Thickness(8, 0, 8, 0), Height = 26, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
            b.SetResourceReference(FrameworkElement.StyleProperty, "PillButton");
            b.Click += (_, _) => { if (when() is DateTime utc) Add(utc); else Hint(hint, L.T("Poné primero un vencimiento."), true); };
            presets.Children.Add(b);
        }
        Preset(L.T("En 1 hora"), () => DateTime.UtcNow.AddHours(1));
        Preset(L.T("Mañana 9:00"), () => ReminderScheduler.TomorrowMorning());
        if (due != null)
            Preset(L.T("Al vencer"), () => due() is { date: DateOnly d } x ? Clock.ToUtc(d, x.time ?? new TimeOnly(9, 0)) : null, L.T("El día del vencimiento, a su hora (o a las 9:00)"));
        // Any day from the calendar: the box gets "día 9:00" to adjust the hour and Enter.
        var pickDay = new Button { Padding = new Thickness(8, 0, 8, 0), Height = 26, Margin = new Thickness(0, 0, 6, 6), ToolTip = L.T("Elegí el día en el calendario y ajustá la hora") };
        var pickRow = new StackPanel { Orientation = Orientation.Horizontal };
        var cal = Ui.Glyph("", 11, "Accent"); cal.Margin = new Thickness(0, 0, 6, 0);
        pickRow.Children.Add(cal); pickRow.Children.Add(new TextBlock { Text = L.T("Elegir día…"), VerticalAlignment = VerticalAlignment.Center });
        pickDay.Content = pickRow;
        pickDay.SetResourceReference(FrameworkElement.StyleProperty, "PillButton");
        pickDay.Click += (_, _) => DatePopup.Open(pickDay, null, d =>
        {
            if (d is not DateOnly day) return;
            box.Text = Fmt.DateBox(day) + " 9:00";
            box.Focus(); box.Select(box.Text.Length - 4, 4);
            Hint(hint, L.T("Ajustá la hora y Enter."), false);
        }, allowEmpty: false);
        presets.Children.Add(pickDay);
        block.Children.Add(presets);
        box.ToolTip = L.T("Por ejemplo: mañana 15:00, viernes 9, 18hs");
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || box.Text.Trim().Length == 0) return;
            e.Handled = true;
            var p = QuickParse.Parse(box.Text, Clock.Today);
            if (p.Title.Length > 0 || (p.Date == null && p.Time == null)) { Hint(hint, L.T("No entiendo cuándo. Probá: mañana 15:00, viernes 9, 18hs"), true); return; }
            Add(Clock.ToUtc(p.Date ?? Clock.Today, p.Time ?? new TimeOnly(9, 0)));
        };
        block.Children.Add(TaskEditor.Placeholder(box, L.T("o escribí cuándo: mañana 15:00")));
        hint.Margin = new Thickness(0, 4, 0, 0);
        block.Children.Add(hint);
        block.Tag = (Action)Reload;
        Reload();
        return block;
    }

    static void Hint(TextBlock hint, string text, bool error)
    {
        hint.Text = text;
        hint.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorInk" : "Muted");
    }

    /// <summary>Today's block: what rings today, and what rang before and was never answered.</summary>
    public static FrameworkElement? TodayBlock(DateOnly today)
    {
        var start = Clock.ToUtc(today, TimeOnly.MinValue);
        var end = Clock.ToUtc(today.AddDays(1), TimeOnly.MinValue);
        var items = Reminders.Unanswered(start).Concat(Reminders.Between(start, end)).GroupBy(r => r.Id).Select(g => g.First()).ToList();
        if (items.Count == 0) return null;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Label(L.T("Recordatorios") + "  ·  " + items.Count, new Thickness(4, 14, 0, 6)));
        foreach (var r in items) stack.Children.Add(Row(r, today));
        return stack;
    }

    static FrameworkElement Row(Reminder r, DateOnly today)
    {
        var grid = new Grid { MinHeight = 38, Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var local = Clock.ToLocal(r.DueUtc);
        bool past = r.DueUtc <= DateTime.UtcNow;
        string when = DateOnly.FromDateTime(local) == today ? Fmt.Time(TimeOnly.FromDateTime(local)) : Fmt.Day(DateOnly.FromDateTime(local), today);
        var time = Ui.Text(when, past ? "Accent" : "Muted", 12.5, mono: true);
        time.Margin = new Thickness(12, 0, 0, 0);
        grid.Children.Add(time);
        var glyph = Ui.Glyph(r.Kind switch { "task" => "", "topic" => "", _ => "" }, 12);
        Grid.SetColumn(glyph, 1);
        grid.Children.Add(glyph);
        var title = Ui.Text(r.Title, "Ink");
        title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(title, 2);
        grid.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) };
        buttons.Children.Add(Ui.IconButton("", (s, _) => SnoozeMenu(r, (FrameworkElement)s!), L.T("Posponer")));
        buttons.Children.Add(Ui.IconButton("", (_, _) => ReminderScheduler.Handle("done", r.Id), r.Kind == "task" ? L.T("Listo (completa el pendiente)") : L.T("Listo")));
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(buttons);
        grid.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Button) App.Current.OpenReminder(r.Id); };
        var frame = new Border { Child = grid, Margin = new Thickness(0, 0, 0, 2), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, past ? "AccentCardRing" : "FaintBorder");
        frame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
        return frame;
    }

    public static void SnoozeMenu(Reminder r, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (label, action) in new[] { (L.T("10 minutos"), "snooze10"), (L.T("1 hora"), "snooze60"), (L.T("Mañana 9:00"), "tomorrow") })
        {
            var m = new MenuItem { Header = label };
            m.Click += (_, _) => ReminderScheduler.Handle(action, r.Id);
            menu.Items.Add(m);
        }
        menu.IsOpen = true;
    }
}
