using System.Globalization;
using System.Text.RegularExpressions;

namespace BuzzDesk.Domain;

/// <summary>What quick capture (and the "add a task" box) understood from one line of text.</summary>
public sealed record Parsed(string Title, int? Priority, DateOnly? Date, TimeOnly? Time, IReadOnlyList<string> Tags, string? Url);

/// <summary>
/// Reads a line like "Revisar backups de PROD !3 mañana 9:30 #dba" into title, priority (!1 low … !3 high),
/// day (hoy/today, mañana/tomorrow, pasado mañana, weekday names, dd/mm[/yyyy], yyyy-mm-dd, +3d, +1w), time
/// (9:30, 18hs, 6pm) and tags (#tag). Spanish and English words both work, whatever the UI language.
/// Matched pieces are removed from the title; a URL is kept in the title and also returned.
/// </summary>
public static partial class QuickParse
{
    /// <summary>"3/12" is month/day (English UI) instead of day/month (Spanish UI).</summary>
    public static bool MonthFirst { get; set; }

    static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lunes"] = DayOfWeek.Monday, ["martes"] = DayOfWeek.Tuesday, ["miercoles"] = DayOfWeek.Wednesday, ["miércoles"] = DayOfWeek.Wednesday,
        ["jueves"] = DayOfWeek.Thursday, ["viernes"] = DayOfWeek.Friday, ["sabado"] = DayOfWeek.Saturday, ["sábado"] = DayOfWeek.Saturday, ["domingo"] = DayOfWeek.Sunday,
        ["lun"] = DayOfWeek.Monday, ["mar"] = DayOfWeek.Tuesday, ["mie"] = DayOfWeek.Wednesday, ["mié"] = DayOfWeek.Wednesday, ["jue"] = DayOfWeek.Thursday, ["vie"] = DayOfWeek.Friday,
        ["monday"] = DayOfWeek.Monday, ["tuesday"] = DayOfWeek.Tuesday, ["wednesday"] = DayOfWeek.Wednesday, ["thursday"] = DayOfWeek.Thursday,
        ["friday"] = DayOfWeek.Friday, ["saturday"] = DayOfWeek.Saturday, ["sunday"] = DayOfWeek.Sunday,
        ["mon"] = DayOfWeek.Monday, ["tue"] = DayOfWeek.Tuesday, ["wed"] = DayOfWeek.Wednesday, ["thu"] = DayOfWeek.Thursday, ["fri"] = DayOfWeek.Friday,
    };

    [GeneratedRegex(@"(?<=^|\s)!([0-3])(?=\s|$)")] private static partial Regex PriorityRx();
    [GeneratedRegex(@"(?<=^|\s)#([\p{L}\p{N}_\-\.]+)")] private static partial Regex TagRx();
    [GeneratedRegex(@"\b(https?://\S+|mailto:\S+|file:\S+)", RegexOptions.IgnoreCase)] private static partial Regex UrlRx();
    [GeneratedRegex(@"(?<=^|\s)(?:a las |at )?([01]?\d|2[0-3])(?::([0-5]\d))?\s?(hs|h|am|pm)?(?=\s|$)", RegexOptions.IgnoreCase)] private static partial Regex TimeRx();
    [GeneratedRegex(@"(?<=^|\s)(\d{4})-(\d{2})-(\d{2})(?=\s|$)")] private static partial Regex IsoDateRx();
    [GeneratedRegex(@"(?<=^|\s)(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?(?=\s|$)")] private static partial Regex DmyRx();
    [GeneratedRegex(@"(?<=^|\s)\+(\d{1,3})\s?(d|w|s|m)(?=\s|$)", RegexOptions.IgnoreCase)] private static partial Regex OffsetRx();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex Spaces();

    public static Parsed Parse(string text, DateOnly today)
    {
        string s = " " + (text ?? "").Trim() + " ";
        int? priority = null; DateOnly? date = null; TimeOnly? time = null; var tags = new List<string>();
        string? url = UrlRx().Match(s) is { Success: true } um ? um.Value : null;
        string urlless = url == null ? s : s.Replace(url, " \u0001 ");

        urlless = PriorityRx().Replace(urlless, m => { priority = int.Parse(m.Groups[1].Value); return " "; });
        urlless = TagRx().Replace(urlless, m => { if (!tags.Contains(m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)) tags.Add(m.Groups[1].Value); return " "; });

        // Dates: explicit formats first, then words.
        urlless = IsoDateRx().Replace(urlless, m =>
        {
            if (date != null || !DateOnly.TryParseExact(m.Value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return m.Value;
            date = d; return " ";
        });
        urlless = DmyRx().Replace(urlless, m =>
        {
            if (date != null) return m.Value;
            int day = int.Parse(m.Groups[1].Value), month = int.Parse(m.Groups[2].Value);
            if (MonthFirst) (day, month) = (month, day);
            int year = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : today.Year;
            if (year < 100) year += 2000;
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return m.Value;
            var d = new DateOnly(year, month, day);
            if (!m.Groups[3].Success && d < today) d = d.AddYears(1);   // "3/1" in December means next January
            date = d; return " ";
        });
        urlless = OffsetRx().Replace(urlless, m =>
        {
            if (date != null) return m.Value;
            int n = int.Parse(m.Groups[1].Value);
            date = char.ToLowerInvariant(m.Groups[2].Value[0]) switch { 'd' => today.AddDays(n), 'm' => today.AddMonths(n), _ => today.AddDays(7 * n) };
            return " ";
        });
        urlless = Words(urlless, today, ref date);

        urlless = TimeRx().Replace(urlless, m =>
        {
            if (time != null) return m.Value;
            bool hasMinutes = m.Groups[2].Success, hasSuffix = m.Groups[3].Success;
            if (!hasMinutes && !hasSuffix && !m.Value.TrimStart().StartsWith("a las", StringComparison.OrdinalIgnoreCase) && !m.Value.TrimStart().StartsWith("at ", StringComparison.OrdinalIgnoreCase)) return m.Value;   // a bare number is not a time
            int h = int.Parse(m.Groups[1].Value), min = hasMinutes ? int.Parse(m.Groups[2].Value) : 0;
            string suffix = m.Groups[3].Value.ToLowerInvariant();
            if (suffix == "pm" && h < 12) h += 12;
            if (suffix == "am" && h == 12) h = 0;
            time = new TimeOnly(h, min);
            return " ";
        });

        string title = Spaces().Replace(urlless.Replace("\u0001", url ?? ""), " ").Trim();
        if (title.Length == 0 && url != null) title = url;
        return new Parsed(title, priority, date, time, tags, url);
    }

    static string Words(string s, DateOnly today, ref DateOnly? date)
    {
        if (date != null) return s;
        (string pattern, DateOnly value)[] fixedWords =
        [
            ("pasado mañana", today.AddDays(2)), ("pasado manana", today.AddDays(2)), ("day after tomorrow", today.AddDays(2)),
            ("mañana", today.AddDays(1)), ("manana", today.AddDays(1)), ("tomorrow", today.AddDays(1)),
            ("hoy", today), ("today", today),
            ("la semana que viene", today.AddDays(7)), ("próxima semana", today.AddDays(7)), ("next week", today.AddDays(7)),
        ];
        foreach (var (pattern, value) in fixedWords)
        {
            var rx = new Regex(@"(?<=^|\s)" + Regex.Escape(pattern) + @"(?=\s|$)", RegexOptions.IgnoreCase);
            if (rx.IsMatch(s)) { date = value; return rx.Replace(s, " ", 1); }
        }
        foreach (var (word, dow) in Weekdays)
        {
            var rx = new Regex(@"(?<=^|\s)(?:el |on |next |este |próximo |proximo )?" + Regex.Escape(word) + @"(?=\s|$)", RegexOptions.IgnoreCase);
            if (!rx.IsMatch(s)) continue;
            int ahead = ((int)dow - (int)today.DayOfWeek + 7) % 7;
            date = today.AddDays(ahead == 0 ? 7 : ahead);   // "lunes" said on a Monday means next Monday
            return rx.Replace(s, " ", 1);
        }
        return s;
    }

    /// <summary>A date typed in a date box: the same words and formats as above, or empty for "no date".
    /// Returns false when the text is not a date.</summary>
    public static bool TryDate(string text, DateOnly today, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var p = Parse(text, today);
        if (p.Date == null || p.Title.Length > 0) return false;
        date = p.Date;
        return true;
    }

    /// <summary>A time typed in a time box ("9", "9:30", "18hs", "6pm"); empty = no time.</summary>
    public static bool TryTime(string text, out TimeOnly? time)
    {
        time = null;
        string t = (text ?? "").Trim();
        if (t.Length == 0) return true;
        if (int.TryParse(t, out int h) && h is >= 0 and <= 23) { time = new TimeOnly(h, 0); return true; }
        var p = Parse(t, DateOnly.MinValue);
        if (p.Time == null || p.Title.Length > 0) return false;
        time = p.Time;
        return true;
    }
}
