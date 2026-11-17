using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>A month of days to pick one: arrows to change month, today ringed, the chosen day filled with the accent.
/// Used by the date drop-down (<see cref="DateField"/>, <see cref="DatePopup"/>).</summary>
public sealed class MiniCalendar : StackPanel
{
    DateOnly month;
    DateOnly? selected;
    readonly TextBlock title = Ui.Text("", "Ink", 13.5, FontWeights.SemiBold);
    readonly UniformGrid days = new() { Columns = 7, Rows = 6 };
    readonly UniformGrid names = new() { Columns = 7, Rows = 1, Margin = new Thickness(0, 8, 0, 2) };
    public event Action<DateOnly>? Picked;

    public MiniCalendar(DateOnly? value)
    {
        selected = value;
        var start = value ?? Clock.Today;
        month = new DateOnly(start.Year, start.Month, 1);
        var head = new DockPanel();
        var prevYear = Ui.IconButton("", (_, _) => Show(month.AddYears(-1)), L.T("Año anterior"));
        var prev = Ui.IconButton("", (_, _) => Show(month.AddMonths(-1)), L.T("Mes anterior"));
        var next = Ui.IconButton("", (_, _) => Show(month.AddMonths(1)), L.T("Mes siguiente"));
        var nextYear = Ui.IconButton("", (_, _) => Show(month.AddYears(1)), L.T("Año siguiente"));
        foreach (var b in new[] { prevYear, prev, next, nextYear }) { b.Width = b.Height = 26; b.FontSize = 11; DockPanel.SetDock(b, Dock.Right); }
        head.Children.Add(nextYear); head.Children.Add(next); head.Children.Add(prev); head.Children.Add(prevYear);
        // The wheel walks the months too.
        MouseWheel += (_, e) => { Show(month.AddMonths(e.Delta > 0 ? -1 : 1)); e.Handled = true; };
        title.Margin = new Thickness(4, 0, 0, 0);
        head.Children.Add(title);
        Children.Add(head);
        foreach (var n in WeekdayNames())
        {
            var t = Ui.Text(n, "Muted", 11);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            names.Children.Add(t);
        }
        Children.Add(names);
        Children.Add(days);
        Show(month);
    }

    /// <summary>First day of the week in the app's language (Monday in Spanish, Sunday in English).</summary>
    public static DayOfWeek FirstDay => L.Culture.DateTimeFormat.FirstDayOfWeek;

    public static IEnumerable<string> WeekdayNames()
    {
        var f = L.Culture.DateTimeFormat;
        for (int i = 0; i < 7; i++) yield return Fmt.Cap(f.ShortestDayNames[((int)FirstDay + i) % 7]);
    }

    /// <summary>The first cell of a month grid: the week start on or before the 1st.</summary>
    public static DateOnly GridStart(DateOnly firstOfMonth)
    {
        int back = ((int)firstOfMonth.DayOfWeek - (int)FirstDay + 7) % 7;
        return firstOfMonth.AddDays(-back);
    }

    void Show(DateOnly m)
    {
        month = m;
        title.Text = Fmt.Cap(m.ToString("MMMM yyyy", L.Culture));
        days.Children.Clear();
        var today = Clock.Today;
        var d = GridStart(m);
        for (int i = 0; i < 42; i++, d = d.AddDays(1))
        {
            var day = d;
            bool inMonth = d.Month == m.Month, isSel = selected == d, isToday = d == today;
            var label = new TextBlock { Text = d.Day.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
            label.SetResourceReference(TextBlock.ForegroundProperty, isSel ? "OnAccent" : isToday ? "Accent" : inMonth ? "Ink" : "LineNumber");
            if (isSel || isToday) label.FontWeight = FontWeights.SemiBold;
            var cell = new Border { Child = label, Width = 32, Height = 30, Margin = new Thickness(1), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            if (isSel) { cell.SetResourceReference(Border.BackgroundProperty, "Accent"); cell.SetResourceReference(Border.BorderBrushProperty, "Accent"); }
            else if (isToday) cell.SetResourceReference(Border.BorderBrushProperty, "AccentRing");
            else cell.BorderBrush = Brushes.Transparent;
            cell.ToolTip = Fmt.Cap(d.ToString("dddd d MMMM yyyy", L.Culture));
            cell.MouseEnter += (_, _) => { if (!isSel) cell.SetResourceReference(Border.BackgroundProperty, "AlternateRow"); };
            cell.MouseLeave += (_, _) => { if (!isSel) cell.Background = Brushes.Transparent; };
            cell.MouseLeftButtonUp += (_, e) => { selected = day; Show(month); Picked?.Invoke(day); e.Handled = true; };
            days.Children.Add(cell);
        }
    }
}

/// <summary>The drop-down card: a box to type a date ("viernes", "25/11", "+3d"), quick picks and the month.</summary>
public static class DatePopup
{
    /// <summary>Opens the card under <paramref name="anchor"/> (or at the mouse when null). <paramref name="picked"/>
    /// gets the date, or null for "Sin fecha" when <paramref name="allowEmpty"/>.</summary>
    public static Popup Open(UIElement? anchor, DateOnly? current, Action<DateOnly?> picked, bool allowEmpty = true)
    {
        var popup = new Popup
        {
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            PlacementTarget = anchor, Placement = anchor == null ? PlacementMode.MousePoint : PlacementMode.Bottom, VerticalOffset = 4,
        };
        var (card, typed) = Card(current, d => { popup.IsOpen = false; picked(d); }, () => popup.IsOpen = false, allowEmpty);
        popup.Child = card;
        popup.Opened += (_, _) => typed.Focus();
        popup.IsOpen = true;
        return popup;
    }

    /// <summary>The card itself (also used by the screenshots).</summary>
    public static (Border card, TextBox typed) Card(DateOnly? current, Action<DateOnly?> Done, Action close, bool allowEmpty = true)
    {

        var root = new StackPanel { Width = 290 };
        var typed = new TextBox { Padding = new Thickness(8, 6, 8, 6) };
        var hint = Ui.Text("", "ErrorInk", 11);
        typed.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { close(); e.Handled = true; return; }
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (typed.Text.Trim().Length == 0) return;
            if (QuickParse.TryDate(typed.Text, Clock.Today, out var d) && d is DateOnly x) Done(x);
            else hint.Text = L.T("No entiendo esa fecha. Probá: viernes, 25/11, +3d");
        };
        root.Children.Add(TaskEditor.Placeholder(typed, L.T("Escribí: viernes, 25/11, +3d…")));
        hint.Margin = new Thickness(2, 2, 0, 0);
        root.Children.Add(hint);

        var today = Clock.Today;
        int toMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var quick = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 4) };
        var picks = new List<(string glyph, string label, DateOnly? date)>
        {
            ("", L.T("Hoy"), today),
            ("", L.T("Mañana"), today.AddDays(1)),
            ("", L.T("Próximo lunes"), today.AddDays(toMonday == 0 ? 7 : toMonday)),
            ("", L.T("En una semana"), today.AddDays(7)),
        };
        if (allowEmpty) picks.Add(("", L.T("Sin fecha"), null));
        foreach (var (glyph, label, date) in picks)
        {
            var row = new DockPanel();
            var g = Ui.Glyph(glyph, 12, date == null ? "Muted" : "Accent"); g.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(g);
            var right = Ui.Text(date is DateOnly dd ? Fmt.Cap(dd.ToString("ddd", L.Culture)) : "", "LineNumber", 11);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(Ui.Text(label, "Ink", 12.5));
            var b = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 0, 8, 0), MinHeight = 30, Margin = new Thickness(0, 0, 4, 4) };
            b.SetResourceReference(FrameworkElement.StyleProperty, "QuickPick");
            var value = date;
            b.Click += (_, _) => Done(value);
            quick.Children.Add(b);
        }
        root.Children.Add(quick);
        var sep = new Border { Height = 1, Margin = new Thickness(0, 2, 0, 6) };
        sep.SetResourceReference(Border.BackgroundProperty, "FaintBorder");
        root.Children.Add(sep);
        var cal = new MiniCalendar(current);
        cal.Picked += d => Done(d);
        root.Children.Add(cal);

        var card = new Border { Child = root, Padding = new Thickness(10), Margin = new Thickness(0, 0, 14, 16), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        card.SetResourceReference(Border.BorderBrushProperty, "StrongBorder");
        card.SetResourceReference(Border.CornerRadiusProperty, "StripRadius");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.35 };
        card.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "UiFont");
        return (card, typed);
    }
}

/// <summary>A date box that opens the calendar drop-down: shows "Mañana · mié 18 nov" with a calendar glyph.</summary>
public sealed class DateField : Button
{
    DateOnly? date;
    readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock glyph;
    public bool AllowEmpty { get; set; } = true;
    public string EmptyText { get; set; } = L.T("Sin fecha");
    /// <summary>Raised when the person picks a date (not when <see cref="Date"/> is set in code).</summary>
    public event Action<DateOnly?>? DateChanged;

    public DateField()
    {
        SetResourceReference(StyleProperty, "DateFieldButton");
        glyph = Ui.Glyph("", 13, "Accent");
        glyph.Margin = new Thickness(0, 0, 8, 0);
        var row = new DockPanel();
        var drop = Ui.Glyph("", 9, "Muted"); drop.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(drop, Dock.Right);
        row.Children.Add(drop);
        row.Children.Add(glyph);
        row.Children.Add(text);
        Content = row;
        Click += (_, _) => DatePopup.Open(this, date, d => { if (d == date) return; Date = d; DateChanged?.Invoke(d); }, AllowEmpty);
        Paint();
    }

    public DateOnly? Date { get => date; set { date = value; Paint(); } }

    void Paint()
    {
        var today = Clock.Today;
        if (date is DateOnly d)
        {
            string rel = Math.Abs(d.DayNumber - today.DayNumber) <= 1 ? Fmt.Day(d, today) + " · " : "";
            text.Text = rel + Fmt.Cap(d.ToString(d.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", L.Culture));
            text.SetResourceReference(TextBlock.ForegroundProperty, d < today ? "ErrorInk" : "Ink");
        }
        else
        {
            text.Text = EmptyText;
            text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        }
    }
}
