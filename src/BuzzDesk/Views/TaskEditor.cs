using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>The right-hand panel of Tasks: every field of one task, saved as soon as a field is left or changed.</summary>
public sealed class TaskEditor : Border
{
    TaskItem? task;
    bool loading;
    public long? TaskId => task?.Id;

    readonly TextBox title = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Padding = new Thickness(8, 6, 8, 6) };
    readonly ComboBox section = new() { MinWidth = 160 };
    readonly ComboBox priority = new() { MinWidth = 120 };
    readonly DateField dueDate = new() { MinWidth = 190 };
    readonly TextBox dueTime = new() { Width = 70 };
    readonly TextBlock dueHint = Ui.Text("", "Muted", 11.5);
    readonly ComboBox repeat = new() { MinWidth = 170 };
    readonly TextBox interval = new() { Width = 46, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly WrapPanel weekdays = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBox monthDay = new() { Width = 46, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly StackPanel repeatDetail = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock repeatHint = Ui.Text("", "Muted", 11.5);
    readonly TextBox tags = new();
    readonly TextBox notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly StackPanel subtasks = new();
    readonly TextBox newSubtask = new();
    readonly TextBlock footer = Ui.Text("", "Muted", 11, mono: true);
    readonly Button completeButton;
    readonly StackPanel body = new();
    readonly TextBlock empty = Ui.Text("", "Muted");
    /// <summary>Extra blocks other stages plug in (reminders, topic).</summary>
    public StackPanel Extras { get; } = new();

    public TaskEditor()
    {
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(BorderBrushProperty, "FaintBorder");
        SetResourceReference(CornerRadiusProperty, "PanelRadius");
        BorderThickness = new Thickness(1);
        Padding = new Thickness(18, 14, 18, 14);

        empty.Text = L.T("Elegí un pendiente para ver y editar sus detalles.");
        empty.Margin = new Thickness(0, 20, 0, 0);
        empty.TextAlignment = TextAlignment.Center;

        title.LostKeyboardFocus += (_, _) => Save();
        title.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Save(); Keyboard.ClearFocus(); e.Handled = true; } };
        body.Children.Add(title);

        // area and priority
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(Field(L.T("Área o proyecto"), section, new Thickness(0, 0, 12, 0)));
        for (int p = 3; p >= 0; p--) priority.Items.Add(new ComboBoxItem { Content = Fmt.Priority(p), Tag = p });
        row.Children.Add(Field(L.T("Prioridad"), priority));
        body.Children.Add(row);
        section.SelectionChanged += (_, _) => Save();
        priority.SelectionChanged += (_, _) => Save();

        // due date
        body.Children.Add(Ui.Label(L.T("Fecha")));
        var dueRow = new StackPanel { Orientation = Orientation.Horizontal };
        dueRow.Children.Add(dueDate);
        var at = Ui.Text(L.T("a las"), "Muted"); at.Margin = new Thickness(10, 0, 8, 0);
        dueRow.Children.Add(at);
        dueRow.Children.Add(dueTime);
        body.Children.Add(dueRow);
        dueDate.ToolTip = L.T("Elegí el día en el calendario");
        dueTime.ToolTip = L.T("9, 9:30, 18hs… Vacío: sin hora");
        dueDate.DateChanged += _ => SaveDue();
        dueTime.LostKeyboardFocus += (_, _) => SaveDue();
        dueTime.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SaveDue(); e.Handled = true; } };
        dueHint.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(dueHint);

        // repeat
        body.Children.Add(Ui.Label(L.T("Repetir")));
        foreach (var (label, tag) in new[] { (L.T("No se repite"), ""), (L.T("Diaria"), "daily"), (L.T("Semanal"), "weekly"), (L.T("Mensual"), "monthly"), (L.T("Último día hábil del mes"), "last_business_day") })
            repeat.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        body.Children.Add(repeat);
        repeat.SelectionChanged += (_, _) => { if (loading) return; ShowRepeatDetail(); SaveRepeat(); };
        var everyRow = new StackPanel { Orientation = Orientation.Horizontal };
        everyRow.Children.Add(Ui.Text(L.T("Cada"), "Muted"));
        interval.Margin = new Thickness(8, 0, 8, 0);
        everyRow.Children.Add(interval);
        everyRow.Children.Add(new TextBlock { Name = "UnitText", VerticalAlignment = VerticalAlignment.Center });
        repeatDetail.Children.Add(everyRow);
        for (int i = 0; i < 7; i++)
        {
            int bit = 1 << i;
            var chip = new DayChip(L.Culture.DateTimeFormat.AbbreviatedDayNames[(i + 1) % 7], bit);
            chip.Changed += () => SaveRepeat();
            weekdays.Children.Add(chip);
        }
        repeatDetail.Children.Add(weekdays);
        var dayRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        dayRow.Children.Add(Ui.Text(L.T("El día"), "Muted"));
        monthDay.Margin = new Thickness(8, 0, 8, 0);
        dayRow.Children.Add(monthDay);
        dayRow.Children.Add(Ui.Text(L.T("(31 = último día del mes)"), "Muted", 11.5));
        repeatDetail.Children.Add(dayRow);
        interval.LostKeyboardFocus += (_, _) => SaveRepeat();
        monthDay.LostKeyboardFocus += (_, _) => SaveRepeat();
        body.Children.Add(repeatDetail);
        repeatHint.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(repeatHint);

        // tags and notes
        body.Children.Add(Ui.Label(L.T("Etiquetas")));
        tags.ToolTip = L.T("Separadas por coma o espacio: dba, prod, reunión");
        tags.LostKeyboardFocus += (_, _) => Save();
        body.Children.Add(tags);
        body.Children.Add(Extras);
        body.Children.Add(Ui.Label(L.T("Notas")));
        notes.LostKeyboardFocus += (_, _) => Save();
        body.Children.Add(notes);

        // subtasks
        body.Children.Add(Ui.Label(L.T("Subtareas")));
        body.Children.Add(subtasks);
        newSubtask.ToolTip = L.T("Escribí una subtarea y Enter");
        newSubtask.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || task == null || newSubtask.Text.Trim().Length == 0) return;
            Tasks.Add(new Tasks.Draft(newSubtask.Text.Trim(), task.SectionId, ParentId: task.Id), Clock.Today);
            newSubtask.Text = "";
            LoadSubtasks();
            e.Handled = true;
        };
        body.Children.Add(Placeholder(newSubtask, L.T("+ Agregar subtarea")));

        // actions
        var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        completeButton = Ui.ToolButton("", L.T("Completar"), (_, _) => { if (task == null) return; if (task.IsOpen) Actions.Complete(task); else Actions.Reopen(task); }, accent: true);
        actions.Children.Add(completeButton);
        actions.Children.Add(Ui.ToolButton("", L.T("Posponer"), (s, _) => { if (task is { IsOpen: true }) Actions.PostponeMenu(task, (FrameworkElement)s!); }));
        actions.Children.Add(Ui.ToolButton("", L.T("Eliminar"), (_, _) => { if (task != null) Actions.Delete(task); }));
        body.Children.Add(actions);
        footer.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(footer);

        Child = new Grid { Children = { Ui.Scroll(body), empty } };
        // Reminders of the open task.
        var reminders = ReminderUi.EditorBlock(
            () => task == null ? [] : Reminders.ForTask(task.Id),
            utc => Reminders.Add(utc, taskId: task!.Id),
            () => (task?.DueDate, task?.DueTime));
        Extras.Children.Add(reminders);
        TaskLoaded += _ => ((Action)reminders.Tag)();
        Show(null);
    }

    static StackPanel Field(string label, FrameworkElement control, Thickness? margin = null)
    {
        var s = new StackPanel { Margin = margin ?? new Thickness(0) };
        s.Children.Add(Ui.Label(label, new Thickness(0, 6, 0, 6)));
        s.Children.Add(control);
        return s;
    }

    /// <summary>A text box with a muted hint shown while it is empty.</summary>
    public static Grid Placeholder(TextBox box, string hint)
    {
        var h = Ui.Text(hint, "Muted");
        h.IsHitTestVisible = false; h.Margin = new Thickness(10, 0, 0, 0);
        void Sync() => h.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();
        return new Grid { Children = { box, h } };
    }

    // ------------------------------------------------------------------ load
    public void Show(long? id)
    {
        task = id is long x ? Tasks.Get(x) : null;
        body.Visibility = task == null ? Visibility.Collapsed : Visibility.Visible;
        empty.Visibility = task == null ? Visibility.Visible : Visibility.Collapsed;
        if (task != null) Load();
    }

    /// <summary>Re-reads the task after a change elsewhere, unless the person is typing in this panel.</summary>
    public void Reload()
    {
        if (task == null) return;
        if (IsKeyboardFocusWithin) return;
        Show(task.Id);
    }

    void Load()
    {
        var t = task!;
        loading = true;
        try
        {
            title.Text = t.Title;
            section.Items.Clear();
            section.Items.Add(new ComboBoxItem { Content = L.T("(Ninguna)"), Tag = null });
            foreach (var s in Sections.All(includeArchived: true).Where(s => !s.Archived || s.Id == t.SectionId))
                section.Items.Add(new ComboBoxItem { Content = (s.IsArea ? "" : "    ") + s.Name, Tag = s.Id });
            section.SelectedItem = section.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (long?)i.Tag == t.SectionId) ?? section.Items[0];
            priority.SelectedItem = priority.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == t.Priority);
            dueDate.Date = t.DueDate;
            dueTime.Text = t.DueTime is TimeOnly time ? time.ToString("HH:mm") : "";
            ShowDueHint();
            var r = t.Recurrence;
            string freq = r == null ? "" : RecurrenceRule.ToDb(r.Freq);
            repeat.SelectedItem = repeat.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == freq);
            interval.Text = (r?.Interval ?? 1).ToString();
            int mask = r?.WeekdaysMask ?? (t.DueDate is DateOnly d ? RecurrenceRule.Bit(d.DayOfWeek) : 0);
            foreach (DayChip chip in weekdays.Children) chip.IsOn = (mask & chip.Bit) != 0;
            monthDay.Text = (r?.MonthDay ?? t.DueDate?.Day ?? Clock.Today.Day).ToString();
            ShowRepeatDetail();
            tags.Text = string.Join(", ", t.Tags);
            notes.Text = t.Notes ?? "";
            completeButton.Content = CompleteContent(t.IsOpen);
            footer.Text = L.F("Creado {0}", Fmt.When(t.CreatedUtc)) + (t.PostponeCount > 0 ? "  ·  " + L.F("pospuesto {0} veces", t.PostponeCount) : "") +
                          (t.CompletedUtc is DateTime done ? "  ·  " + L.F("hecho {0}", Fmt.When(done)) : "");
            LoadSubtasks();
        }
        finally { loading = false; }
        TaskLoaded?.Invoke(t);
    }

    /// <summary>After the fields are filled (stages plug their extras here).</summary>
    public event Action<TaskItem>? TaskLoaded;

    static StackPanel CompleteContent(bool open)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Ui.Glyph(open ? "" : "", 12, "OnAccent"); g.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(g);
        var label = new TextBlock { Text = open ? L.T("Completar") : L.T("Volver a abrir"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        row.Children.Add(label);
        return row;
    }

    void LoadSubtasks()
    {
        subtasks.Children.Clear();
        if (task == null) return;
        foreach (var s in Tasks.Subtasks(task.Id))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = System.Windows.Media.Brushes.Transparent };
            var check = new CheckBox { IsChecked = !s.IsOpen, Margin = new Thickness(0, 0, 8, 0) };
            check.SetResourceReference(StyleProperty, "TaskCheck");
            var sub = s;
            check.Click += (_, _) => { if (sub.IsOpen) Tasks.Complete(sub.Id, Clock.Today); else Tasks.Reopen(sub.Id); LoadSubtasks(); };
            DockPanel.SetDock(check, Dock.Left);
            row.Children.Add(check);
            var del = Ui.IconButton("", (_, _) => { Tasks.Delete(sub.Id); LoadSubtasks(); }, L.T("Quitar subtarea"));
            del.Opacity = 0;
            row.MouseEnter += (_, _) => del.Opacity = 1;
            row.MouseLeave += (_, _) => del.Opacity = 0;
            DockPanel.SetDock(del, Dock.Right);
            row.Children.Add(del);
            var text = Ui.Text(s.Title, s.IsOpen ? "Ink" : "Muted");
            if (!s.IsOpen) text.TextDecorations = TextDecorations.Strikethrough;
            row.Children.Add(text);
            subtasks.Children.Add(row);
        }
    }

    void ShowDueHint()
    {
        dueHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        dueHint.Text = task?.DueDate is DateOnly d ? Fmt.Cap(d.ToString("dddd d MMMM yyyy", L.Culture)) + (task.IsOverdue(Clock.Today) ? "  ·  " + L.T("atrasado") : "") : L.T("Sin fecha");
    }

    void ShowRepeatDetail()
    {
        string freq = (string)((ComboBoxItem)repeat.SelectedItem).Tag;
        repeatDetail.Visibility = freq is "" or "last_business_day" ? Visibility.Collapsed : Visibility.Visible;
        weekdays.Visibility = freq == "weekly" ? Visibility.Visible : Visibility.Collapsed;
        ((FrameworkElement)monthDay.Parent).Visibility = freq == "monthly" ? Visibility.Visible : Visibility.Collapsed;
        var unit = (TextBlock)((StackPanel)interval.Parent).Children[2];
        unit.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        unit.Text = freq switch { "daily" => L.T("día(s)"), "weekly" => L.T("semana(s)"), _ => L.T("mes(es)") };
        repeatHint.Text = task?.Recurrence is RecurrenceRule r ? Fmt.Rule(r) : "";
    }

    // ------------------------------------------------------------------ save
    RecurrenceRule? ReadRule()
    {
        string freq = (string)((ComboBoxItem)repeat.SelectedItem).Tag;
        if (freq.Length == 0) return null;
        int every = int.TryParse(interval.Text.Trim(), out int n) ? Math.Clamp(n, 1, 365) : 1;
        int mask = weekdays.Children.Cast<DayChip>().Where(c => c.IsOn).Sum(c => c.Bit);
        int? day = int.TryParse(monthDay.Text.Trim(), out int d) ? Math.Clamp(d, 1, 31) : null;
        var f = RecurrenceRule.FromDb(freq);
        return new RecurrenceRule(f, f == Freq.LastBusinessDay ? 1 : every, f == Freq.Weekly && mask > 0 ? mask : null, f == Freq.Monthly ? day : null, task?.Recurrence?.Until);
    }

    void SaveRepeat()
    {
        if (loading || task == null) return;
        var rule = ReadRule();
        var due = task.DueDate;
        if (rule != null && due == null) due = rule.FirstOnOrAfter(Clock.Today);
        Commit(task with { Recurrence = rule, DueDate = due });
    }

    void SaveDue()
    {
        if (loading || task == null) return;
        var date = dueDate.Date;
        if (date == null) dueTime.Text = "";
        if (!QuickParse.TryTime(dueTime.Text, out var time)) { dueHint.Text = L.T("No entiendo esa hora. Probá: 9, 9:30, 18hs"); dueHint.SetResourceReference(TextBlock.ForegroundProperty, "ErrorInk"); return; }
        Commit(task with { DueDate = date, DueTime = date == null ? null : time });
        ShowDueHint();
    }

    void Save()
    {
        if (loading || task == null) return;
        if (title.Text.Trim().Length == 0) { title.Text = task.Title; return; }
        Commit(task with
        {
            Title = title.Text.Trim(),
            SectionId = (long?)((ComboBoxItem?)section.SelectedItem)?.Tag,
            Priority = (int)((ComboBoxItem)priority.SelectedItem).Tag,
            Notes = notes.Text,
        }, tags.Text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).ToList());
    }

    void Commit(TaskItem updated, IReadOnlyList<string>? newTags = null)
    {
        var tagList = newTags ?? task!.Tags;
        bool same = updated == task && tagList.SequenceEqual(task!.Tags, StringComparer.OrdinalIgnoreCase);
        if (same) return;
        Tasks.Update(updated, newTags);
        task = Tasks.Get(updated.Id);
        if (task == null) return;
        loading = true;
        try
        {
            dueDate.Date = task.DueDate;
            dueTime.Text = task.DueTime is TimeOnly t ? t.ToString("HH:mm") : "";
            tags.Text = string.Join(", ", task.Tags);
            ShowRepeatDetail();
        }
        finally { loading = false; }
    }

}
