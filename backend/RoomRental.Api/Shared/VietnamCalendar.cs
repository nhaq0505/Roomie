namespace RoomRental.Api.Services;

public static class VietnamCalendar
{
    public static string CurrentVietnamPeriod() => TodayVietnam().ToString("yyyy-MM");

    public static DateOnly TodayVietnam()
    {
        return ApiSupport.VietnamDate(DateTimeOffset.UtcNow);
    }

}
