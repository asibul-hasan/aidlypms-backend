using System.Globalization;

namespace AidlyPms.Api.Common;

public static class DateHelper
{
    private static readonly string[] Formats = new[]
    {
        "dd-MM-yyyy",
        "dd/MM/yyyy",
        "d-M-yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss",
        "dd-MM-yyyy HH:mm",
        "dd/MM/yyyy HH:mm",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss",
        "o",
        "s"
    };

    public static bool TryParse(string? input, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        input = input.Trim();

        // 1. Try exact known formats (dd-MM-yyyy, dd/MM/yyyy, yyyy-MM-dd, etc.)
        if (DateTime.TryParseExact(input, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        // 2. Try British English culture (which standardizes day-month-year: dd/MM/yyyy and dd-MM-yyyy)
        if (DateTime.TryParse(input, new CultureInfo("en-GB"), DateTimeStyles.None, out date))
        {
            return true;
        }

        // 3. Fallback to general invariant parsing
        return DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static DateTime ToStartOfDayUtc(DateTime date)
    {
        return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
    }

    public static DateTime ToEndOfDayUtc(DateTime date)
    {
        // If time was explicitly passed (not midnight 00:00:00), preserve it
        if (date.TimeOfDay != TimeSpan.Zero)
        {
            return DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }

        // Include entire 24 hours of that date up to 23:59:59.9999999
        return DateTime.SpecifyKind(date.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
    }

    public static readonly TimeZoneInfo DhakaTimeZone = ResolveDhakaTimeZone();

    private static TimeZoneInfo ResolveDhakaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka"); }
        catch { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Bangladesh Standard Time"); }
        catch { }
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Dhaka", TimeSpan.FromHours(6), "Dhaka Time", "Dhaka Standard Time");
    }

    public static DateTime GetDhakaNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, DhakaTimeZone);
    }

    public static DateOnly GetDhakaToday()
    {
        return DateOnly.FromDateTime(GetDhakaNow());
    }

    public static (DateTime StartUtc, DateTime EndUtc) GetDhakaDayUtcRange(DateOnly dhakaDate)
    {
        var startLocal = dhakaDate.ToDateTime(TimeOnly.MinValue);
        var endLocal = dhakaDate.ToDateTime(TimeOnly.MaxValue);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, DhakaTimeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, DhakaTimeZone);
        return (startUtc, endUtc);
    }

    public static (DateTime? fromUtc, DateTime? toUtc) NormalizeDateRange(DateTime? fromDate, DateTime? toDate)
    {
        DateTime? fromUtc = fromDate.HasValue ? ToStartOfDayUtc(fromDate.Value) : null;
        DateTime? toUtc = toDate.HasValue ? ToEndOfDayUtc(toDate.Value) : null;
        return (fromUtc, toUtc);
    }

    public static (DateTime? fromUtc, DateTime? toUtc) NormalizeDateRange(string? fromDateStr, string? toDateStr)
    {
        DateTime? fromUtc = TryParse(fromDateStr, out var fd) ? ToStartOfDayUtc(fd) : null;
        DateTime? toUtc = TryParse(toDateStr, out var td) ? ToEndOfDayUtc(td) : null;
        return (fromUtc, toUtc);
    }
}
