using BuzzDesk.Domain;

namespace BuzzDesk.Tests;

public class RecurrenceTests
{
    static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact] public void Daily_every_two_days() => Assert.Equal(D(2026, 11, 17), new RecurrenceRule(Freq.Daily, 2).Next(D(2026, 11, 15)));

    [Fact]
    public void Weekly_monday_and_thursday()
    {
        int mask = RecurrenceRule.Bit(DayOfWeek.Monday) | RecurrenceRule.Bit(DayOfWeek.Thursday);
        var rule = new RecurrenceRule(Freq.Weekly, 1, mask);
        Assert.Equal(D(2026, 11, 19), rule.Next(D(2026, 11, 16)));   // Mon → Thu
        Assert.Equal(D(2026, 11, 23), rule.Next(D(2026, 11, 19)));   // Thu → next Mon
    }

    [Fact]
    public void Weekly_every_two_weeks_on_friday()
    {
        var rule = new RecurrenceRule(Freq.Weekly, 2, RecurrenceRule.Bit(DayOfWeek.Friday));
        Assert.Equal(D(2026, 12, 4), rule.Next(D(2026, 11, 20)));
    }

    [Fact]
    public void Weekly_without_days_uses_the_anchor_weekday() =>
        Assert.Equal(D(2026, 11, 25), new RecurrenceRule(Freq.Weekly).Next(D(2026, 11, 18)));

    [Fact]
    public void Monthly_31_is_the_last_day()
    {
        var rule = new RecurrenceRule(Freq.Monthly, 1, MonthDay: 31);
        Assert.Equal(D(2026, 2, 28), rule.Next(D(2026, 1, 31)));
        Assert.Equal(D(2026, 3, 31), rule.Next(D(2026, 2, 28)));
        Assert.Equal(D(2028, 2, 29), rule.Next(D(2028, 1, 31)));   // leap year
    }

    [Fact]
    public void Monthly_later_this_month_when_the_day_has_not_come()
    {
        var rule = new RecurrenceRule(Freq.Monthly, 1, MonthDay: 20);
        Assert.Equal(D(2026, 11, 20), rule.Next(D(2026, 11, 15)));
        Assert.Equal(D(2027, 1, 20), new RecurrenceRule(Freq.Monthly, 2, MonthDay: 20).Next(D(2026, 11, 20)));
    }

    [Theory]
    [InlineData(2026, 1, 30)]    // Friday 30, Saturday 31
    [InlineData(2026, 5, 29)]    // May 31 is a Sunday
    [InlineData(2026, 11, 30)]   // Monday
    [InlineData(2026, 10, 30)]   // Oct 31 is a Saturday
    public void Last_business_day(int y, int m, int expected) => Assert.Equal(D(y, m, expected), RecurrenceRule.LastBusinessDay(y, m));

    [Fact]
    public void Last_business_day_series()
    {
        var rule = new RecurrenceRule(Freq.LastBusinessDay);
        Assert.Equal(D(2026, 11, 30), rule.Next(D(2026, 11, 2)));    // still this month
        Assert.Equal(D(2026, 12, 31), rule.Next(D(2026, 11, 30)));   // Thursday
        Assert.Equal(D(2027, 1, 29), rule.Next(D(2026, 12, 31)));    // Jan 31 2027 is a Sunday
    }

    [Fact]
    public void Until_ends_the_series() =>
        Assert.Null(new RecurrenceRule(Freq.Daily, 1, Until: D(2026, 11, 15)).Next(D(2026, 11, 15)));

    [Fact]
    public void First_on_or_after()
    {
        Assert.Equal(D(2026, 11, 16), new RecurrenceRule(Freq.Weekly, 1, RecurrenceRule.Bit(DayOfWeek.Monday)).FirstOnOrAfter(D(2026, 11, 16)));
        Assert.Equal(D(2026, 11, 30), new RecurrenceRule(Freq.LastBusinessDay).FirstOnOrAfter(D(2026, 11, 15)));
    }
}
