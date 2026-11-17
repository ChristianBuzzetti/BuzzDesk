using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>Bills and renewals: what comes due, "Paid" (moves to the next due date), and the month in numbers.</summary>
public sealed class BillsView : DeskView
{
    public override string Id => "bills";
    readonly TextBlock summary = Ui.Text("", "Muted", 12.5);
    readonly StackPanel body = new();
    readonly TextBlock emptyText = Ui.Text("", "Muted");

    public BillsView()
    {
        var root = new DockPanel { Margin = new Thickness(8, 4, 8, 0) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(Ui.Title(L.T("Pagos y vencimientos")));
        summary.Margin = new Thickness(0, 6, 0, 0);
        head.Children.Add(summary);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        emptyText.Margin = new Thickness(4, 16, 0, 0);
        DockPanel.SetDock(emptyText, Dock.Top);
        root.Children.Add(emptyText);
        root.Children.Add(Ui.Scroll(body));
        Children.Add(root);
    }

    public override void BuildToolbar(Panel bar)
    {
        bar.Children.Add(Ui.ToolButton("", L.T("Nuevo vencimiento"), (_, _) => New(), "Ctrl+N"));
        var currency = new ComboBox { MinWidth = 90, VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("Moneda por defecto de los vencimientos nuevos") };
        foreach (var c in Fmt.Currencies.Prepend(Fmt.DefaultCurrency).Distinct()) currency.Items.Add(c);
        currency.SelectedItem = Fmt.DefaultCurrency;
        currency.SelectionChanged += (_, _) => { Store.Settings.DefaultCurrency = (string)currency.SelectedItem; Store.SaveSettings(); };
        var label = Ui.Text(L.T("Moneda"), "Muted", 12); label.Margin = new Thickness(8, 0, 8, 0);
        bar.Children.Add(label);
        bar.Children.Add(currency);
    }

    public override void New() => Edit(null);

    public override void Refresh()
    {
        var today = Clock.Today;
        var bills = Bills.List();
        var totals = Bills.MonthTotals(today.Year, today.Month);
        string month = Fmt.Cap(today.ToString("MMMM", L.Culture));
        summary.Text = totals.Count == 0 ? month : month + ":  " + string.Join("   ·   ", totals.Select(t =>
            L.F("pagado {0}", Fmt.Money(t.paid, t.currency)) + (t.pending > 0 ? "  ·  " + L.F("por pagar {0}", Fmt.Money(t.pending, t.currency)) : "")));
        body.Children.Clear();
        var monthEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        (string, string, Func<Bill, bool>)[] groups =
        [
            (L.T("Vencidos"), "ErrorInk", b => b.NextDue < today),
            (L.T("Próximos 7 días"), "Accent", b => b.NextDue >= today && b.NextDue <= today.AddDays(7)),
            (L.T("Resto del mes"), "Muted", b => b.NextDue > today.AddDays(7) && b.NextDue < monthEnd),
            (L.T("Más adelante"), "Muted", b => b.NextDue >= monthEnd && b.NextDue > today.AddDays(7)),
            (L.T("Sin fecha"), "Muted", b => b.NextDue == null),
        ];
        foreach (var (label, brush, match) in groups)
        {
            var g = bills.Where(match).ToList();
            if (g.Count == 0) continue;
            var l = Ui.Label(label + "  ·  " + g.Count, new Thickness(4, 14, 0, 6));
            l.SetResourceReference(TextBlock.ForegroundProperty, brush);
            body.Children.Add(l);
            foreach (var b in g) body.Children.Add(Row(b, today));
        }
        var paid = Bills.Payments(today.Year, today.Month);
        if (paid.Count > 0)
        {
            body.Children.Add(Ui.Label(L.T("Pagado este mes") + "  ·  " + paid.Count, new Thickness(4, 20, 0, 6)));
            foreach (var p in paid)
            {
                var row = new DockPanel { Margin = new Thickness(10, 2, 4, 2) };
                var undo = Ui.IconButton("", (_, _) => Bills.Unpay(p.Id), L.T("Deshacer el pago"));
                DockPanel.SetDock(undo, Dock.Right);
                row.Children.Add(undo);
                var amount = Ui.Text(p.AmountCents is long a ? Fmt.Money(a, p.Currency) : "", "Muted", 12, mono: true);
                amount.Margin = new Thickness(10, 0, 10, 0);
                DockPanel.SetDock(amount, Dock.Right);
                row.Children.Add(amount);
                var ok = Ui.Glyph("", 12, "Ok"); ok.Margin = new Thickness(0, 0, 10, 0);
                row.Children.Add(ok);
                row.Children.Add(Ui.Text(p.BillName + "  ·  " + Fmt.Day(p.DueDate, today), "Muted"));
                body.Children.Add(row);
            }
        }
        emptyText.Text = bills.Count == 0 ? L.T("Cargá tus servicios, la tarjeta, el seguro o la renovación del DNI: BuzzDesk te avisa unos días antes y lleva la cuenta de lo pagado.") : "";
        emptyText.Visibility = bills.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static FrameworkElement Row(Bill b, DateOnly today)
    {
        var grid = new Grid { MinHeight = 46, Background = System.Windows.Media.Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var g = Ui.Glyph(b.IsRenewal ? "" : "", 14, b.Overdue(today) ? "ErrorInk" : "Muted");
        g.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(g);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
        text.Children.Add(Ui.Text(b.Name, "Ink", weight: FontWeights.SemiBold));
        var meta = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (b.Category != null) meta.Children.Add(Ui.Chip(b.Category));
        if (b.Recurrence is RecurrenceRule r) meta.Children.Add(Ui.Chip(Bills.IsYearly(r) ? L.T("Anual") : Fmt.Rule(r), "AlternateRow", "Muted"));
        if (b.IsRenewal) meta.Children.Add(Ui.Chip(L.T("Renovación"), "AlternateRow", "Muted"));
        text.Children.Add(meta);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var amount = Ui.Text(b.AmountCents is long a ? Fmt.Money(a, b.Currency) : "", "Ink", 13, mono: true);
        amount.Margin = new Thickness(8, 0, 12, 0);
        Grid.SetColumn(amount, 2);
        grid.Children.Add(amount);
        string due = b.NextDue is DateOnly d ? (d < today ? L.F("venció {0}", Fmt.Day(d, today).ToLower(L.Culture)) : Fmt.Day(d, today)) : "";
        var dueText = Ui.Text(due, b.Overdue(today) ? "ErrorInk" : b.DueSoon(today) ? "Accent" : "Muted", 12, mono: true);
        dueText.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(dueText, 3);
        grid.Children.Add(dueText);
        var pay = Ui.ToolButton("", b.IsRenewal ? L.T("Renovado") : L.T("Pagado"), (_, _) => Pay(b, null), accent: b.DueSoon(today) || b.Overdue(today));
        pay.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(pay, 4);
        grid.Children.Add(pay);
        grid.ContextMenu = Menu(b);
        grid.MouseLeftButtonUp += (_, e) => { if (e.ClickCount == 2) Edit(b); };
        var frame = new Border { Child = grid, Margin = new Thickness(0, 0, 0, 2), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, b.Overdue(today) ? "ErrorInk" : "FaintBorder");
        frame.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
        return frame;
    }

    static void Pay(Bill b, long? amount)
    {
        long payment = Bills.Pay(b.Id, amount, Clock.Today);
        var next = Bills.Get(b.Id)?.NextDue;
        App.Current.Desk?.SetStatus(L.F("Pagado: {0}", b.Name) + (next is DateOnly n ? "  ·  " + L.F("próximo {0}", Fmt.Day(n, Clock.Today).ToLower(L.Culture)) : ""), L.T("Deshacer"), () => Bills.Unpay(payment));
    }

    static ContextMenu Menu(Bill b)
    {
        var menu = new ContextMenu();
        void Add(string header, Action a) { var m = new MenuItem { Header = header }; m.Click += (_, _) => a(); menu.Items.Add(m); }
        Add(b.IsRenewal ? L.T("Renovado") : L.T("Pagado"), () => Pay(b, null));
        Add(L.T("Pagado con otro monto…"), () =>
        {
            if (PromptWindow.Ask(App.Current.Desk, L.T("Pagado"), L.F("¿Cuánto pagaste? ({0})", b.Currency), b.AmountCents is long a ? (a / 100m).ToString("0.##", L.Culture) : "") is { } text)
            {
                if (Fmt.TryMoney(text, out var cents)) Pay(b, cents);
                else App.Current.Desk?.SetStatus(L.T("No entiendo ese monto."));
            }
        });
        Add(L.T("Editar…"), () => Edit(b));
        Add(L.T("Historial de pagos"), () =>
        {
            var lines = Bills.History(b.Id).Select(p => Fmt.Cap(p.DueDate.ToString("d MMM yyyy", L.Culture)) + ": " + (p.AmountCents is long a ? Fmt.Money(a, p.Currency) : "—"));
            PromptWindow.Info(App.Current.Desk, b.Name, L.T("Pagos registrados") + "\n\n" + (lines.Any() ? string.Join("\n", lines) : L.T("Todavía ninguno.")));
        });
        menu.Items.Add(new Separator());
        Add(L.T("Archivar"), () => Bills.Archive(b.Id, true));
        return menu;
    }

    /// <summary>New or edit: name, payment or renewal, amount and currency, next due date, repeat, notice days.</summary>
    public static void Edit(Bill? b)
    {
        var form = new FormWindow(b == null ? L.T("Nuevo vencimiento") : L.T("Editar vencimiento"));
        var name = form.Add(L.T("Nombre"), new TextBox { Text = b?.Name ?? "" }, L.T("Luz, internet, tarjeta, seguro del auto, DNI, pasaporte…"));
        var kind = form.Add(L.T("Tipo"), new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left });
        kind.Items.Add(new ComboBoxItem { Content = L.T("Pago"), Tag = "payment" });
        kind.Items.Add(new ComboBoxItem { Content = L.T("Renovación (documento, licencia)"), Tag = "renewal" });
        kind.SelectedIndex = b?.IsRenewal == true ? 1 : 0;
        var amountRow = new StackPanel { Orientation = Orientation.Horizontal };
        var amount = new TextBox { Width = 160, Text = b?.AmountCents is long a ? (a / 100m).ToString("0.##", L.Culture) : "" };
        var currency = new ComboBox { Width = 90, Margin = new Thickness(8, 0, 0, 0), IsEditable = true };
        foreach (var c in Fmt.Currencies.Prepend(Fmt.DefaultCurrency).Distinct()) currency.Items.Add(c);
        currency.Text = b?.Currency ?? Fmt.DefaultCurrency;
        amountRow.Children.Add(amount); amountRow.Children.Add(currency);
        form.Add(L.T("Monto (opcional)"), amountRow);
        var due = form.Add(L.T("Próximo vencimiento"), new DateField { Date = b?.NextDue });
        var repeat = form.Add(L.T("Se repite"), new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left });
        foreach (var (label, value) in new[] { (L.T("No se repite"), ""), (L.T("Todos los meses"), "m1"), (L.T("Cada 2 meses"), "m2"), (L.T("Cada 3 meses"), "m3"), (L.T("Cada 6 meses"), "m6"), (L.T("Todos los años"), "y"), (L.T("Todas las semanas"), "w") })
            repeat.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        string currentTag = b?.Recurrence switch { null => "", { Freq: Freq.Weekly } => "w", var r when Bills.IsYearly(r) => "y", { Freq: Freq.Monthly } r => "m" + r.Interval, _ => "m1" };
        repeat.SelectedItem = repeat.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == currentTag) ?? repeat.Items[b == null ? 1 : 0];
        var remind = form.Add(L.T("Avisar días antes"), new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Text = (b?.RemindDaysBefore ?? 3).ToString() });
        var category = form.Add(L.T("Categoría (opcional)"), new TextBox { Text = b?.Category ?? "" }, L.T("Servicios, impuestos, suscripciones, documentos…"));
        var notes = form.Add(L.T("Notas"), new TextBox { Text = b?.Notes ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 });

        DateOnly? dueDate = null; long? cents = null; int days = 3;
        if (!form.Ask(App.Current.Desk, () =>
        {
            if (name.Text.Trim().Length == 0) return L.T("Poné un nombre.");
            if (!Fmt.TryMoney(amount.Text, out cents)) return L.T("No entiendo ese monto.");
            string cur = currency.Text.Trim().ToUpperInvariant();
            if (cur.Length != 3 || !cur.All(char.IsAsciiLetterUpper)) return L.T("La moneda va en código de 3 letras: ARS, USD, EUR…");
            dueDate = due.Date;
            if (!int.TryParse(remind.Text, out days) || days is < 0 or > 60) return L.T("Avisar: de 0 a 60 días antes.");
            if ((string)((ComboBoxItem)repeat.SelectedItem).Tag != "" && dueDate == null) return L.T("Para que se repita necesita una fecha de vencimiento.");
            return null;
        })) return;
        string tag = (string)((ComboBoxItem)repeat.SelectedItem).Tag;
        var anchor = dueDate ?? Clock.Today;
        RecurrenceRule? rule = tag switch
        {
            "" => null,
            "y" => Bills.Yearly(anchor),
            "w" => new RecurrenceRule(Freq.Weekly, 1, RecurrenceRule.Bit(anchor.DayOfWeek)),
            _ => new RecurrenceRule(Freq.Monthly, int.Parse(tag[1..]), MonthDay: anchor.Day),
        };
        var bill = (b ?? new Bill()) with
        {
            Name = name.Text.Trim(), Kind = (string)((ComboBoxItem)kind.SelectedItem).Tag, AmountCents = cents, Currency = currency.Text.Trim().ToUpperInvariant(),
            NextDue = dueDate, Recurrence = rule, RemindDaysBefore = days, Category = category.Text, Notes = notes.Text,
        };
        if (b == null) Bills.Add(bill); else Bills.Update(bill);
    }

    /// <summary>Today's block: overdue and coming due within their notice days.</summary>
    public static FrameworkElement? TodayBlock(DateOnly today)
    {
        var due = Bills.List().Where(b => b.NextDue != null && (b.Overdue(today) || b.DueSoon(today))).ToList();
        if (due.Count == 0) return null;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Label(L.T("Pagos y vencimientos") + "  ·  " + due.Count, new Thickness(4, 14, 0, 6)));
        foreach (var b in due) stack.Children.Add(Row(b, today));
        return stack;
    }
}
