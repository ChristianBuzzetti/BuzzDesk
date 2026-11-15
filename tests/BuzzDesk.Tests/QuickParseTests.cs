using BuzzDesk.Domain;

namespace BuzzDesk.Tests;

[Collection("db")]
public class QuickParseTests
{
    static readonly DateOnly Today = new(2026, 11, 15);   // a Sunday

    [Fact]
    public void Full_line()
    {
        var p = QuickParse.Parse("Revisar backups de PROD !3 mañana 9:30 #dba #prod", Today);
        Assert.Equal("Revisar backups de PROD", p.Title);
        Assert.Equal(3, p.Priority);
        Assert.Equal(new DateOnly(2026, 11, 16), p.Date);
        Assert.Equal(new TimeOnly(9, 30), p.Time);
        Assert.Equal(["dba", "prod"], p.Tags);
    }

    [Theory]
    [InlineData("Llamar a Juan hoy", 2026, 11, 15)]
    [InlineData("Call Ana tomorrow", 2026, 11, 16)]
    [InlineData("Informe el viernes", 2026, 11, 20)]
    [InlineData("Informe domingo", 2026, 11, 22)]       // said on a Sunday: next Sunday
    [InlineData("Pagar 3/12", 2026, 12, 3)]
    [InlineData("Pagar 3/1", 2027, 1, 3)]              // already past this year
    [InlineData("Plan 2027-02-01", 2027, 2, 1)]
    [InlineData("Ver esto +3d", 2026, 11, 18)]
    [InlineData("Ver esto +2w", 2026, 11, 29)]
    [InlineData("Turno pasado mañana", 2026, 11, 17)]
    public void Dates(string text, int y, int m, int d) => Assert.Equal(new DateOnly(y, m, d), QuickParse.Parse(text, Today).Date);

    [Theory]
    [InlineData("Reunión 18hs", 18, 0)]
    [InlineData("Meeting 6pm", 18, 0)]
    [InlineData("Daily a las 10", 10, 0)]
    [InlineData("Llamar 14:45", 14, 45)]
    public void Times(string text, int h, int min) => Assert.Equal(new TimeOnly(h, min), QuickParse.Parse(text, Today).Time);

    [Fact]
    public void A_bare_number_is_not_a_time()
    {
        var p = QuickParse.Parse("Comprar 3 cables", Today);
        Assert.Null(p.Time);
        Assert.Equal("Comprar 3 cables", p.Title);
    }

    [Fact]
    public void Url_stays_in_the_title_and_is_returned()
    {
        var p = QuickParse.Parse("Grafana https://grafana.local/d/abc #mon", Today);
        Assert.Equal("https://grafana.local/d/abc", p.Url);
        Assert.Equal("Grafana https://grafana.local/d/abc", p.Title);
        Assert.Equal(["mon"], p.Tags);
    }

    [Fact]
    public void Date_and_time_boxes()
    {
        Assert.True(QuickParse.TryDate("mañana", Today, out var d)); Assert.Equal(new DateOnly(2026, 11, 16), d);
        Assert.True(QuickParse.TryDate("", Today, out d)); Assert.Null(d);
        Assert.False(QuickParse.TryDate("cualquier cosa", Today, out _));
        Assert.True(QuickParse.TryTime("9", out var t)); Assert.Equal(new TimeOnly(9, 0), t);
        Assert.True(QuickParse.TryTime("18:30", out t)); Assert.Equal(new TimeOnly(18, 30), t);
        Assert.False(QuickParse.TryTime("25:00", out _));
    }
}

[Collection("db")]   // MonthFirst is static: keep it away from the parallel QuickParse tests
public class QuickParseMonthFirstTests
{
    [Fact]
    public void English_order()
    {
        QuickParse.MonthFirst = true;
        try { Assert.Equal(new DateOnly(2026, 12, 3), QuickParse.Parse("Pay 12/3", new DateOnly(2026, 11, 15)).Date); }
        finally { QuickParse.MonthFirst = false; }
    }
}
