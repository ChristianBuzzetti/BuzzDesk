namespace BuzzDesk.Domain;

public enum Freq { Daily, Weekly, Monthly, LastBusinessDay }

/// <summary>
/// A repeat rule (table recurrence). Each instance of a series is a task with its own due date; completing one
/// creates the next with <see cref="Next"/>. Business days are Monday to Friday (holidays: phase 2).
/// </summary>
public sealed record RecurrenceRule(Freq Freq, int Interval = 1, int? WeekdaysMask = null, int? MonthDay = null, DateOnly? Until = null)
{
    public static string ToDb(Freq f) => f switch { Freq.Daily => "daily", Freq.Weekly => "weekly", Freq.Monthly => "monthly", _ => "last_business_day" };
    public static Freq FromDb(string s) => s switch { "daily" => Freq.Daily, "weekly" => Freq.Weekly, "monthly" => Freq.Monthly, "last_business_day" => Freq.LastBusinessDay, _ => throw new ArgumentException("Unknown frequency: " + s) };

    /// <summary>Bit for a weekday in <see cref="WeekdaysMask"/> (bit0 = Monday … bit6 = Sunday).</summary>
    public static int Bit(DayOfWeek d) => 1 << (((int)d + 6) % 7);

    /// <summary>The rule as the database keeps it (weekly without days = the anchor's weekday; monthly without a
    /// day = the anchor's day).</summary>
    public RecurrenceRule Normalized(DateOnly anchor) => Freq switch
    {
        Freq.Weekly => this with { WeekdaysMask = WeekdaysMask is > 0 and <= 127 ? WeekdaysMask : Bit(anchor.DayOfWeek), MonthDay = null },
        Freq.Monthly => this with { MonthDay = MonthDay is >= 1 and <= 31 ? MonthDay : anchor.Day, WeekdaysMask = null },
        _ => this with { WeekdaysMask = null, MonthDay = null },
    };

    /// <summary>The next due date strictly after <paramref name="from"/> (the current instance's due date), or null
    /// when the series ended (<see cref="Until"/>).</summary>
    public DateOnly? Next(DateOnly from)
    {
        int n = Math.Max(1, Interval);
        DateOnly next;
        switch (Freq)
        {
            case Freq.Daily:
                next = from.AddDays(n);
                break;
            case Freq.Weekly:
            {
                int mask = WeekdaysMask is > 0 and <= 127 ? WeekdaysMask.Value : Bit(from.DayOfWeek);
                var week0 = WeekStart(from);
                next = from;
                for (int i = 1; i <= 7 * n + 7; i++)
                {
                    var d = from.AddDays(i);
                    int weeks = (WeekStart(d).DayNumber - week0.DayNumber) / 7;
                    if (weeks % n == 0 && (mask & Bit(d.DayOfWeek)) != 0) { next = d; break; }
                }
                break;
            }
            case Freq.Monthly:
            {
                int day = MonthDay is >= 1 and <= 31 ? MonthDay.Value : from.Day;
                var candidate = DayInMonth(from.Year, from.Month, day);
                next = candidate > from ? candidate : DayInMonth(AddMonths(from, n), day);
                break;
            }
            default:
            {
                var candidate = LastBusinessDay(from.Year, from.Month);
                next = candidate > from ? candidate : LastBusinessDay(AddMonths(from, n));
                break;
            }
        }
        return Until is DateOnly u && next > u ? null : next;
    }

    /// <summary>First occurrence on or after <paramref name="day"/> (a new recurring task without a due date).</summary>
    public DateOnly? FirstOnOrAfter(DateOnly day)
    {
        if (Freq == Freq.Daily) return Until is DateOnly u && day > u ? null : day;
        return Next(day.AddDays(-1));
    }

    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    static (int y, int m) AddMonths(DateOnly d, int months) { var x = new DateOnly(d.Year, d.Month, 1).AddMonths(months); return (x.Year, x.Month); }
    static DateOnly DayInMonth((int y, int m) ym, int day) => DayInMonth(ym.y, ym.m, day);
    /// <summary>Day N of the month, or its last day when the month is shorter (31 = always the last day).</summary>
    public static DateOnly DayInMonth(int y, int m, int day) => new(y, m, Math.Min(day, DateTime.DaysInMonth(y, m)));
    static DateOnly LastBusinessDay((int y, int m) ym) => LastBusinessDay(ym.y, ym.m);
    public static DateOnly LastBusinessDay(int y, int m)
    {
        var d = new DateOnly(y, m, DateTime.DaysInMonth(y, m));
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
        return d;
    }
}
