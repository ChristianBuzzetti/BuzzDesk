using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>A topic in full: status, who we wait on, next review, area, tags, description, reminders, its tasks
/// and the timeline of notes (status changes and reviews are logged by themselves).</summary>
public sealed class TopicEditor : Border
{
    Topic? topic;
    bool loading;
    public long? TopicId => topic?.Id;

    readonly TextBox title = new() { FontSize = 17, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(8, 6, 8, 6) };
    readonly StackPanel statusRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBlock waitingLine = Ui.Text("", "Muted", 12.5);
    readonly ComboBox section = new() { MinWidth = 170 };
    readonly TextBox review = new() { MinWidth = 130 };
    readonly TextBlock reviewHint = Ui.Text("", "Muted", 11.5);
    readonly TextBox tags = new();
    readonly TextBox description = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly StackPanel taskList = new();
    readonly TextBox newTask = new();
    readonly TextBox newNote = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly StackPanel timeline = new();
    readonly StackPanel body = new();
    readonly TextBlock empty = Ui.Text("", "Muted");
    readonly StackPanel reminders;
    /// <summary>Extra blocks other stages plug in (links).</summary>
    public StackPanel Extras { get; } = new();
    public event Action<Topic>? TopicLoaded;

    public TopicEditor()
    {
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(BorderBrushProperty, "FaintBorder");
        SetResourceReference(CornerRadiusProperty, "PanelRadius");
        BorderThickness = new Thickness(1);
        Padding = new Thickness(18, 14, 18, 14);
        empty.Text = L.T("Elegí un tema para ver su línea de tiempo, o creá uno nuevo.");
        empty.TextAlignment = TextAlignment.Center;
        empty.Margin = new Thickness(0, 20, 0, 0);

        title.LostKeyboardFocus += (_, _) => Save();
        title.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Save(); Keyboard.ClearFocus(); e.Handled = true; } };
        body.Children.Add(title);

        foreach (var (label, state) in new[] { (L.T("Activo"), TopicState.Active), (L.T("Esperando…"), TopicState.Waiting), (L.T("En pausa"), TopicState.Paused), (L.T("Cerrado"), TopicState.Closed) })
        {
            var chip = new RadioButton { Content = label, GroupName = "TopicStatus" + GetHashCode(), Tag = state };
            chip.SetResourceReference(StyleProperty, "SegmentChip");
            chip.Click += (_, _) => ChangeStatus(state);
            statusRow.Children.Add(chip);
        }
        body.Children.Add(statusRow);
        waitingLine.Margin = new Thickness(2, 6, 0, 0);
        body.Children.Add(waitingLine);

        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var secField = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        secField.Children.Add(Ui.Label(L.T("Área o proyecto"), new Thickness(0, 10, 0, 6)));
        secField.Children.Add(section);
        row.Children.Add(secField);
        var revField = new StackPanel();
        revField.Children.Add(Ui.Label(L.T("Próxima revisión"), new Thickness(0, 10, 0, 6)));
        var revRow = new StackPanel { Orientation = Orientation.Horizontal };
        revRow.Children.Add(review);
        var reviewed = Ui.ToolButton("", L.T("Revisado"), (s, _) => ReviewedMenu((FrameworkElement)s!), L.T("Anota la revisión y elige la próxima fecha"));
        reviewed.Margin = new Thickness(8, 0, 0, 0);
        revRow.Children.Add(reviewed);
        revField.Children.Add(revRow);
        row.Children.Add(revField);
        body.Children.Add(row);
        reviewHint.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(reviewHint);
        section.SelectionChanged += (_, _) => Save();
        review.ToolTip = L.T("hoy, mañana, viernes, 25/11, +1w… Vacío: sin revisión");
        review.LostKeyboardFocus += (_, _) => Save();
        review.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Save(); e.Handled = true; } };

        body.Children.Add(Ui.Label(L.T("Etiquetas")));
        tags.ToolTip = L.T("Separadas por coma o espacio: dba, prod, reunión");
        tags.LostKeyboardFocus += (_, _) => Save();
        body.Children.Add(tags);
        body.Children.Add(Ui.Label(L.T("Descripción")));
        description.LostKeyboardFocus += (_, _) => Save();
        body.Children.Add(description);

        reminders = ReminderUi.EditorBlock(() => topic == null ? [] : Reminders.ForTopic(topic.Id), utc => Reminders.Add(utc, topicId: topic!.Id));
        body.Children.Add(reminders);
        body.Children.Add(Extras);

        body.Children.Add(Ui.Label(L.T("Pendientes del tema")));
        body.Children.Add(taskList);
        newTask.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || topic == null || newTask.Text.Trim().Length == 0) return;
            var p = QuickParse.Parse(newTask.Text, Clock.Today);
            if (p.Title.Length == 0) return;
            Tasks.Add(new Tasks.Draft(p.Title, topic.SectionId, p.Priority ?? 0, p.Date, p.Time, Tags: p.Tags, TopicId: topic.Id), Clock.Today);
            newTask.Text = "";
            LoadTasks();
            e.Handled = true;
        };
        body.Children.Add(TaskEditor.Placeholder(newTask, L.T("+ Agregar un pendiente a este tema")));

        body.Children.Add(Ui.Label(L.T("Línea de tiempo")));
        newNote.ToolTip = L.T("Ctrl+Enter para agregar");
        newNote.KeyDown += (_, e) => { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { AddNote(); e.Handled = true; } };
        body.Children.Add(TaskEditor.Placeholder(newNote, L.T("¿Qué pasó? (Ctrl+Enter)")));
        var addNote = Ui.ToolButton("", L.T("Agregar nota"), (_, _) => AddNote());
        addNote.Margin = new Thickness(0, 6, 0, 8);
        addNote.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(addNote);
        body.Children.Add(timeline);

        var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        actions.Children.Add(Ui.ToolButton("", L.T("Eliminar tema"), (_, _) => Delete()));
        body.Children.Add(actions);

        Child = new Grid { Children = { Ui.Scroll(body), empty } };
        Show(null);
    }

    public void Show(long? id)
    {
        topic = id is long x ? Topics.Get(x) : null;
        body.Visibility = topic == null ? Visibility.Collapsed : Visibility.Visible;
        empty.Visibility = topic == null ? Visibility.Visible : Visibility.Collapsed;
        if (topic != null) Load();
    }

    public void Reload()
    {
        if (topic == null || IsKeyboardFocusWithin) return;
        Show(topic.Id);
    }

    void Load()
    {
        var t = topic!;
        loading = true;
        try
        {
            title.Text = t.Title;
            foreach (RadioButton chip in statusRow.Children) chip.IsChecked = (TopicState)chip.Tag == t.Status;
            waitingLine.Text = WaitingText(t);
            waitingLine.Visibility = waitingLine.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            section.Items.Clear();
            section.Items.Add(new ComboBoxItem { Content = L.T("(Ninguna)"), Tag = null });
            foreach (var s in Sections.All(includeArchived: true).Where(s => !s.Archived || s.Id == t.SectionId))
                section.Items.Add(new ComboBoxItem { Content = (s.IsArea ? "" : "    ") + s.Name, Tag = s.Id });
            section.SelectedItem = section.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (long?)i.Tag == t.SectionId) ?? section.Items[0];
            review.Text = Fmt.DateBox(t.NextReview);
            ShowReviewHint();
            tags.Text = string.Join(", ", t.Tags);
            description.Text = t.Description ?? "";
            ((Action)reminders.Tag)();
            LoadTasks();
            LoadTimeline();
        }
        finally { loading = false; }
        TopicLoaded?.Invoke(t);
    }

    public static string WaitingText(Topic t)
    {
        if (t.Status == TopicState.Waiting && t.WaitingOn != null)
        {
            int days = t.WaitingSinceUtc is DateTime since ? (int)(DateTime.UtcNow - since).TotalDays : 0;
            return days < 1 ? L.F("Esperando a {0} desde hoy", t.WaitingOn) : L.F("Esperando a {0} hace {1} días", t.WaitingOn, days);
        }
        if (t.Status == TopicState.Closed && t.ClosedUtc is DateTime closed) return L.F("Cerrado {0}", Fmt.When(closed));
        return "";
    }

    void ShowReviewHint()
    {
        reviewHint.SetResourceReference(TextBlock.ForegroundProperty, topic?.ReviewDue(Clock.Today) == true ? "Accent" : "Muted");
        reviewHint.Text = topic?.NextReview is DateOnly d ? Fmt.Cap(d.ToString("dddd d MMMM yyyy", L.Culture)) + (topic.ReviewDue(Clock.Today) ? "  ·  " + L.T("toca revisarlo") : "") : L.T("Sin fecha de revisión");
    }

    void LoadTasks()
    {
        taskList.Children.Clear();
        if (topic == null) return;
        foreach (var t in Tasks.List(new TaskFilter(TaskScope.All, TopicId: topic.Id, Limit: 100), Clock.Today).OrderBy(t => !t.IsOpen))
            taskList.Children.Add(TaskRows.Build(t, Clock.Today, x => Host()?.OpenTask(x.Id)));
    }

    MainWindow? Host() => Window.GetWindow(this) as MainWindow;

    void LoadTimeline()
    {
        timeline.Children.Clear();
        if (topic == null) return;
        foreach (var n in Topics.Timeline(topic.Id))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = System.Windows.Media.Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, n.Kind == "note" ? "Accent" : "StrongBorder");
            row.Children.Add(dot);
            var text = new StackPanel();
            text.Children.Add(Ui.Text(Fmt.When(n.CreatedUtc) + "  ·  " + Fmt.Ago(n.CreatedUtc), "Muted", 11, mono: true));
            var b = Ui.Text(n.Body, n.Kind == "note" ? "Ink" : "Muted");
            if (n.Kind != "note") b.FontStyle = FontStyles.Italic;
            b.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(b);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (n.Kind == "note")
            {
                var note = n;
                var del = Ui.IconButton("", (_, _) =>
                {
                    Topics.DeleteNote(note.Id);
                    LoadTimeline();
                    Host()?.SetStatus(L.T("Nota eliminada"), L.T("Deshacer"), () => { Topics.RestoreNote(note.Id); LoadTimeline(); });
                }, L.T("Eliminar nota"));
                del.Opacity = 0;
                row.MouseEnter += (_, _) => del.Opacity = 1;
                row.MouseLeave += (_, _) => del.Opacity = 0;
                Grid.SetColumn(del, 2);
                row.Children.Add(del);
            }
            timeline.Children.Add(row);
        }
    }

    void AddNote()
    {
        if (topic == null || newNote.Text.Trim().Length == 0) return;
        Topics.AddNote(topic.Id, newNote.Text);
        newNote.Text = "";
        LoadTimeline();
    }

    void ChangeStatus(TopicState state)
    {
        if (topic == null) return;
        string? person = null;
        if (state == TopicState.Waiting)
        {
            var known = People.All().Select(p => p.Name).ToList();
            string hint = known.Count > 0 ? "\n\n" + L.T("Personas anteriores:") + " " + string.Join(", ", known.Take(8)) : "";
            person = PromptWindow.Ask(Window.GetWindow(this), L.T("Esperando a…"), L.T("¿A quién estás esperando?") + hint, topic.WaitingOn ?? "");
            if (string.IsNullOrWhiteSpace(person)) { Load(); return; }
            person = known.FirstOrDefault(k => k.Equals(person.Trim(), StringComparison.OrdinalIgnoreCase)) ?? person.Trim();
        }
        string note = state switch
        {
            TopicState.Waiting => L.F("Esperando a {0}", person!),
            TopicState.Paused => L.T("En pausa"),
            TopicState.Closed => L.T("Cerrado"),
            _ => topic.Status == TopicState.Closed ? L.T("Reabierto") : L.T("Activo"),
        };
        Topics.SetStatus(topic.Id, state, person, note);
        Show(topic.Id);
    }

    void ReviewedMenu(FrameworkElement anchor)
    {
        if (topic == null) return;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var today = Clock.Today;
        foreach (var (label, date) in new (string, DateOnly?)[] { (L.T("Revisar mañana"), today.AddDays(1)), (L.T("En 3 días"), today.AddDays(3)), (L.T("En una semana"), today.AddDays(7)), (L.T("En dos semanas"), today.AddDays(14)), (L.T("En un mes"), today.AddMonths(1)), (L.T("Sin próxima revisión"), null) })
        {
            var m = new MenuItem { Header = label, InputGestureText = date is DateOnly d ? Fmt.Day(d, today) : "" };
            m.Click += (_, _) =>
            {
                Topics.Reviewed(topic.Id, date, date is DateOnly next ? L.F("Revisado. Próxima revisión: {0}", Fmt.Cap(next.ToString("dddd d/M", L.Culture))) : L.T("Revisado."));
                Show(topic.Id);
            };
            menu.Items.Add(m);
        }
        menu.IsOpen = true;
    }

    void Save()
    {
        if (loading || topic == null) return;
        if (title.Text.Trim().Length == 0) { title.Text = topic.Title; return; }
        if (!QuickParse.TryDate(review.Text, Clock.Today, out var next))
        {
            reviewHint.Text = L.T("No entiendo esa fecha. Probá: hoy, mañana, viernes, 25/11, +3d");
            reviewHint.SetResourceReference(TextBlock.ForegroundProperty, "ErrorInk");
            return;
        }
        var updated = topic with
        {
            Title = title.Text.Trim(), Description = description.Text, NextReview = next,
            SectionId = (long?)((ComboBoxItem?)section.SelectedItem)?.Tag,
        };
        var newTags = tags.Text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (updated == topic && newTags.SequenceEqual(topic.Tags, StringComparer.OrdinalIgnoreCase)) return;
        Topics.Update(updated, newTags);
        topic = Topics.Get(topic.Id);
        loading = true;
        try { review.Text = Fmt.DateBox(topic?.NextReview); tags.Text = string.Join(", ", topic?.Tags ?? []); ShowReviewHint(); }
        finally { loading = false; }
    }

    void Delete()
    {
        if (topic == null) return;
        var t = topic;
        Topics.Delete(t.Id);
        Show(null);
        Host()?.SetStatus(L.F("Tema eliminado: {0}", t.Title), L.T("Deshacer"), () => Topics.Restore(t.Id));
    }
}
