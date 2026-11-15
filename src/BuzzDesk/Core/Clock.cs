namespace BuzzDesk;

/// <summary>The app's time zone: the one chosen in Personalize, or Windows' own. Instants are stored in UTC and
/// shown in this zone; calendar dates (due dates, review dates) are days in this zone.</summary>
public static class Clock
{
    static TimeZoneInfo? zone;

    public static TimeZoneInfo Zone => zone ??= Resolve(Store.Settings.TimeZoneId);

    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Local; }
    }

    /// <summary>Changes the zone (empty = follow Windows) and saves it.</summary>
    public static void SetZone(string id)
    {
        Store.Settings.TimeZoneId = id ?? "";
        Store.SaveSettings();
        zone = null;
        Changed?.Invoke();
    }

    /// <summary>Windows changed its zone (only matters while following Windows).</summary>
    public static void SystemZoneChanged() { TimeZoneInfo.ClearCachedData(); zone = null; Changed?.Invoke(); }

    public static event Action? Changed;

    public static DateTime UtcNow => DateTime.UtcNow;
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>A wall-clock time in the app's zone to UTC. Skipped times (DST gap) move forward an hour.</summary>
    public static DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    public static DateTime ToUtc(DateOnly day, TimeOnly time) => ToUtc(day.ToDateTime(time));

    /// <summary>"(UTC-03:00) Buenos Aires" for the zone in use.</summary>
    public static string Describe(TimeZoneInfo z) => z.DisplayName;
}
