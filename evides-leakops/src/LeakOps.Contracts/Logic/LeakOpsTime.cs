using System.Globalization;

namespace LeakOps.Contracts.Logic;

/// <summary>Time helpers. Everything is stored in UTC; "local" is only used for day type and rush hours.</summary>
public static class LeakOpsTime
{
    // Local time is always Amsterdam time, with daylight saving, whatever the time zone of the machine or container the code runs on.
    private static readonly TimeZoneInfo Amsterdam = FindAmsterdam();

    private static TimeZoneInfo FindAmsterdam()
    {
        foreach (var id in new[] { "Europe/Amsterdam", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Amsterdam (fixed)", TimeSpan.FromHours(1), "Amsterdam (fixed)", "Amsterdam (fixed)");   // last resort: winter time
    }

    /// <summary>The same instant with a zero offset. Cosmos DB compares timestamps as strings, so queries must always use this form.</summary>
    public static DateTimeOffset Utc(DateTimeOffset value) => new(value.UtcDateTime, TimeSpan.Zero);

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(Utc(utc), Amsterdam);

    public static string DayType(DateTimeOffset utc) =>
        ToLocal(utc).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "weekend" : "weekday";

    /// <summary>Parses an ISO timestamp. Text without an offset is treated as UTC.</summary>
    public static bool TryParseUtc(string? text, out DateTimeOffset utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return false;
        utc = Utc(parsed);
        return true;
    }
}
