using System.Windows;
using System.Windows.Controls;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>Birthdays, anniversaries and other yearly dates, in the order they come; a notice a few days before.</summary>
public sealed class DatesView : DeskView
{
    public override string Id => "dates";
    readonly StackPanel body = new();
    readonly TextBlock emptyText = Ui.Text("", "Muted");

    public DatesView()
    {
        var root = new DockPanel { Margin = new Thickness(8, 4, 8, 0) };
        var head = Ui.Title(L.T("Fechas importantes"));
        head.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        emptyText.Margin = new Thickness(4, 16, 0, 0);
        DockPanel.SetDock(emptyText, Dock.Top);
        root.Children.Add(emptyText);
        root.Children.Add(Ui.Scroll(body));
        Children.Add(root);
    }

    public override void BuildToolbar(Panel bar) => bar.Children.Add(Ui.ToolButton("", L.T("Nueva fecha"), (_, _) => New(), "Ctrl+N"));
    public override void New() => Edit(null);

    public override void Refresh()
    {
        var today = Clock.Today;
        var all = Dates.All().Select(d => (d, on: d.Next(today))).OrderBy(x => x.on).ThenBy(x => x.d.Label).ToList();
        body.Children.Clear();
        foreach (var month in all.GroupBy(x => new DateOnly(x.on.Year, x.on.Month, 1)))
        {
            body.Children.Add(Ui.Label(Fmt.Cap(month.Key.ToString("MMMM yyyy", L.Culture)), new Thickness(4, 14, 0, 6)));
            foreach (var (d, on) in month) body.Children.Add(Row(d, on, today));
        }
        emptyText.Text = all.Count == 0 ? L.T("Cumpleaños, aniversarios y fechas que no te querés olvidar: BuzzDesk te avisa unos días antes, todos los años.") : "";
        emptyText.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static string Describe(ImportantDate d, DateOnly on, DateOnly today)
    {
        int days = on.DayNumber - today.DayNumber;
        string when = days == 0 ? L.T("hoy") : days == 1 ? L.T("mañana") : L.F("en {0} días", days);
        int? years = d.YearsOn(on);
        string what = d.Kind switch
        {
            "birthday" => years is int y ? L.F("cumple {0}", y) : L.T("cumpleaños"),
            "anniversary" => years is int y2 ? L.F("{0} años", y2) : L.T("aniversario"),
            _ => "",
        };
        return when + (what.Length > 0 ? " · " + what : "");
    }

    static FrameworkElement Row(ImportantDate d, DateOnly on, DateOnly today)
    {
        var grid = new Grid { MinHeight = 44, Background = System.Windows.Media.Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var day = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        day.Children.Add(Ui.Text(on.Day.ToString(), on.DayNumber - today.DayNumber <= d.RemindDaysBefore ? "Accent" : "Ink", 17, FontWeights.SemiBold, mono: true));
        day.Children.Add(Ui.Text(on.ToString("ddd", L.Culture), "Muted", 10.5, mono: true));
        grid.Children.Add(day);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
        text.Children.Add(Ui.Text((d.Kind == "birthday" ? "🎂 " : d.Kind == "anniversary" ? "💍 " : "📅 ") + d.Label, "Ink", weight: FontWeights.SemiBold));
        if (d.PersonName != null && !d.Label.Contains(d.PersonName, StringComparison.CurrentCultureIgnoreCase)) text.Children.Add(Ui.Text(d.PersonName, "Muted", 12));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var when = Ui.Text(Describe(d, on, today), on.DayNumber - today.DayNumber <= d.RemindDaysBefore ? "Accent" : "Muted", 12, mono: true);
        when.Margin = new Thickness(8, 0, 12, 0);
        Grid.SetColumn(when, 2);
        grid.Children.Add(when);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.IconButton("", (_, _) => Edit(d), L.T("Editar")));
        buttons.Children.Add(Ui.IconButton("", (_, _) =>
        {
            if (PromptWindow.Confirm(App.Current.Desk, L.T("Eliminar fecha"), L.F("¿Eliminar \"{0}\"?", d.Label), L.T("Eliminar"), L.T("Cancelar"))) Dates.Delete(d.Id);
        }, L.T("Eliminar")));
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(buttons);
        var frame = new Border { Child = grid, Margin = new Thickness(0, 0, 0, 2), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
        frame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
        return frame;
    }

    public static void Edit(ImportantDate? d)
    {
        var form = new FormWindow(d == null ? L.T("Nueva fecha") : L.T("Editar fecha"));
        var kind = form.Add(L.T("Tipo"), new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left });
        foreach (var (text, tag) in new[] { (L.T("Cumpleaños"), "birthday"), (L.T("Aniversario"), "anniversary"), (L.T("Otra"), "other") })
            kind.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
        kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == (d?.Kind ?? "birthday"));
        var person = form.Add(L.T("Persona (opcional)"), new TextBox { Text = d?.PersonName ?? "" });
        var label = form.Add(L.T("Nombre"), new TextBox { Text = d?.Label ?? "" }, L.T("Vacío: \"Cumple de\" y la persona"));
        // The calendar picks day and month; the year counts only when "I know the year" is on (for the age).
        var date = form.Add(L.T("Fecha"), new DateField { AllowEmpty = false, EmptyText = L.T("Elegí el día"),
            Date = d == null ? null : new DateOnly(d.Year ?? Clock.Today.Year, d.Month, d.Month == 2 && d.Day == 29 && !DateTime.IsLeapYear(d.Year ?? Clock.Today.Year) ? 28 : d.Day) });
        var knowYear = new CheckBox { Content = L.T("Sé el año (para calcular la edad)"), IsChecked = d?.Year != null };
        form.AddRaw(knowYear);
        var remind = form.Add(L.T("Avisar días antes"), new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Text = (d?.RemindDaysBefore ?? 3).ToString() });
        int month = 0, day = 0, days = 3; int? year = null;
        if (!form.Ask(App.Current.Desk, () =>
        {
            if (date.Date is not DateOnly picked) return L.T("Elegí el día en el calendario.");
            (day, month) = (picked.Day, picked.Month);
            year = knowYear.IsChecked == true ? picked.Year : null;
            if (year is int yr && yr < 1900) return L.T("Esa fecha no existe.");
            if (label.Text.Trim().Length == 0 && person.Text.Trim().Length == 0) return L.T("Poné un nombre o una persona.");
            if (!int.TryParse(remind.Text, out days) || days is < 0 or > 60) return L.T("Avisar: de 0 a 60 días antes.");
            return null;
        })) return;
        string k = (string)((ComboBoxItem)kind.SelectedItem).Tag;
        string name = label.Text.Trim().Length > 0 ? label.Text.Trim()
            : k == "birthday" ? L.F("Cumple de {0}", person.Text.Trim()) : k == "anniversary" ? L.F("Aniversario con {0}", person.Text.Trim()) : person.Text.Trim();
        if (d == null) Dates.Add(name, k, month, day, year, person.Text, days);
        else Dates.Update(d with { Label = name, Kind = k, Month = month, Day = day, Year = year, RemindDaysBefore = days }, person.Text);
    }

    /// <summary>Today's block: what comes within the next 7 days (or its notice days).</summary>
    public static FrameworkElement? TodayBlock(DateOnly today)
    {
        var soon = Dates.Upcoming(today, 14).Where(x => x.on.DayNumber - today.DayNumber <= Math.Max(7, x.date.RemindDaysBefore)).ToList();
        if (soon.Count == 0) return null;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Label(L.T("Fechas que se acercan"), new Thickness(4, 14, 0, 6)));
        foreach (var (d, on) in soon)
        {
            var row = new DockPanel { Margin = new Thickness(10, 3, 4, 3) };
            var when = Ui.Text(Describe(d, on, today), on == today ? "Accent" : "Muted", 12, mono: true);
            DockPanel.SetDock(when, Dock.Right);
            row.Children.Add(when);
            row.Children.Add(Ui.Text((d.Kind == "birthday" ? "🎂 " : d.Kind == "anniversary" ? "💍 " : "📅 ") + d.Label, "Ink"));
            stack.Children.Add(row);
        }
        return stack;
    }
}
