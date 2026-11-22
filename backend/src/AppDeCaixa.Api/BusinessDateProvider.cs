namespace AppDeCaixa.Api;

public sealed class BusinessDateProvider(TimeZoneInfo timeZone)
{
    public DateTime LocalNow => DateTime.SpecifyKind(
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime,
        DateTimeKind.Unspecified);

    public DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
}
