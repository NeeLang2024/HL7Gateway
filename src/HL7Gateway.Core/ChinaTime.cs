namespace HL7Gateway.Core;

public static class ChinaTime
{
    private static readonly TimeZoneInfo Tz;

    static ChinaTime()
    {
        try
        {
            Tz = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch
        {
            Tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);
}
