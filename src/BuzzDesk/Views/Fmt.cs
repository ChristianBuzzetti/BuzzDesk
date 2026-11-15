using BuzzDesk.Data;
using BuzzDesk.Domain;

namespace BuzzDesk;

/// <summary>Dates, times and labels as people read them, in the app's language.</summary>
public static class Fmt
{
    public static string Day(DateOnly d, DateOnly today)
    {
        int diff = d.DayNumber - today.DayNumber;
        return diff switch
        {
            0 => L.T("Hoy"),
            1 => L.T("Mañana"),
            -1 => L.T("Ayer"),
            > 1 and < 7 => Cap(d.ToString("dddd", L.Culture)),
            _ => Cap(d.ToString(d.Year == today.Year ? (L.Es ? "ddd d/M" : "ddd M/d") : (L.Es ? "d/M/yyyy" : "M/d/yyyy"), L.Culture)),
        };
    }

    public static string Time(TimeOnly t) => t.ToString(L.Es ? "HH:mm" : "h:mm tt", L.Culture);

    public static string Due(TaskItem t, DateOnly today)
    {
        if (t.DueDate is not DateOnly d) return "";
        string s = Day(d, today);
        if (t.IsOverdue(today))
        {
            int late = today.DayNumber - d.DayNumber;
            s = late == 1 ? L.T("Ayer") : L.F("Hace {0} días", late);
        }
        return t.DueTime is TimeOnly time ? s + " · " + Time(time) : s;
    }

    /// <summary>Date for editable boxes, in the order QuickParse reads back: 17/11/2026 or 11/17/2026.</summary>
    public static string DateBox(DateOnly? d) => d is DateOnly x ? x.ToString(L.Es ? "d/M/yyyy" : "M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture) : "";

    public static string Priority(int p) => p switch { 3 => L.T("Alta"), 2 => L.T("Media"), 1 => L.T("Baja"), _ => L.T("Sin prioridad") };
    /// <summary>Theme brush for a priority marker.</summary>
    public static string PriorityBrush(int p) => p switch { 3 => "ErrorInk", 2 => "SqlNumber", 1 => "SqlString", _ => "FaintBorder" };

    public static string Rule(RecurrenceRule r)
    {
        string every(string one, string many) => r.Interval == 1 ? L.T(one) : L.F(many, r.Interval);
        return r.Freq switch
        {
            Freq.Daily => every("Todos los días", "Cada {0} días"),
            Freq.Weekly => every("Todas las semanas", "Cada {0} semanas") + (r.WeekdaysMask is int m ? ": " + Weekdays(m) : ""),
            Freq.Monthly => every("Todos los meses", "Cada {0} meses") + (r.MonthDay is int day ? (day == 31 ? L.T(", el último día") : L.F(", el día {0}", day)) : ""),
            _ => L.T("El último día hábil del mes"),
        };
    }

    public static string Weekdays(int mask)
    {
        var names = new List<string>();
        for (int i = 0; i < 7; i++)
            if ((mask & (1 << i)) != 0) names.Add(L.Culture.DateTimeFormat.AbbreviatedDayNames[(i + 1) % 7]);
        return string.Join(", ", names);
    }

    /// <summary>The currency for new bills: the chosen one, else Windows' region's (ARS in Argentina).</summary>
    public static string DefaultCurrency
    {
        get
        {
            if (Store.Settings.DefaultCurrency is { Length: 3 } c) return c;
            try { return new System.Globalization.RegionInfo(System.Globalization.CultureInfo.CurrentCulture.Name).ISOCurrencySymbol; }
            catch (ArgumentException) { return "USD"; }
        }
    }

    public static readonly string[] Currencies = ["ARS", "USD", "EUR", "BRL", "UYU", "CLP", "PYG", "BOB", "MXN", "COP", "PEN", "GBP", "CAD", "AUD", "JPY", "CHF"];

    /// <summary>"ARS 25.000,00" in the app's number format.</summary>
    public static string Money(long cents, string currency) => currency + " " + (cents / 100m).ToString(cents % 100 == 0 ? "N0" : "N2", L.Culture);

    /// <summary>Reads "25000", "25.000,50" or "25,000.50" (the app's format first). Empty = no amount.</summary>
    public static bool TryMoney(string text, out long? cents)
    {
        cents = null;
        string t = (text ?? "").Trim().TrimStart('$').Trim();
        if (t.Length == 0) return true;
        if (decimal.TryParse(t, System.Globalization.NumberStyles.Number, L.Culture, out var v) ||
            decimal.TryParse(t, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out v))
        {
            if (v < 0 || v > 1_000_000_000_000m) return false;
            cents = (long)Math.Round(v * 100m, MidpointRounding.AwayFromZero);
            return true;
        }
        return false;
    }

    public static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], L.Culture) + s[1..];

    /// <summary>"hace 3 días" / "3 days ago" for an instant.</summary>
    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return L.T("recién");
        if (span.TotalHours < 1) return L.F("hace {0} min", (int)span.TotalMinutes);
        if (span.TotalDays < 1) return L.F("hace {0} h", (int)span.TotalHours);
        if (span.TotalDays < 2) return L.T("ayer");
        return L.F("hace {0} días", (int)span.TotalDays);
    }

    /// <summary>An instant in the app's zone: "Hoy 14:30", "lun 17/11 09:00".</summary>
    public static string When(DateTime utc)
    {
        var local = Clock.ToLocal(utc);
        return Day(DateOnly.FromDateTime(local), Clock.Today) + " " + Time(TimeOnly.FromDateTime(local));
    }
}
