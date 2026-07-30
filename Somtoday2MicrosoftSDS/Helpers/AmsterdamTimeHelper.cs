namespace Somtoday2MicrosoftSDS.Helpers;

internal static class AmsterdamTimeHelper
{
    private static readonly TimeZoneInfo AmsterdamTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    internal static DateOnly GetDate(DateTimeOffset instant)
    {
        DateTimeOffset amsterdamTime = TimeZoneInfo.ConvertTime(instant, AmsterdamTimeZone);
        return DateOnly.FromDateTime(amsterdamTime.DateTime);
    }

    internal static string GetSchoolYear(DateOnly date)
    {
        return date.Month < 8
            ? $"{date.Year - 1}-{date.Year}"
            : $"{date.Year}-{date.Year + 1}";
    }
}
