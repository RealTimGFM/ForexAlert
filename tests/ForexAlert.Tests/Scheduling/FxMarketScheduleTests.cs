using ForexAlert.Scheduling;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Scheduling;

public sealed class FxMarketScheduleTests
{
    [Theory]
    [InlineData(2026, 7, 10, 20, 59, 59, true)]
    [InlineData(2026, 7, 10, 21, 0, 0, false)]
    [InlineData(2026, 7, 11, 16, 0, 0, false)]
    [InlineData(2026, 7, 12, 20, 59, 59, false)]
    [InlineData(2026, 7, 12, 21, 0, 0, true)]
    public void IsOpen_UsesExactFridayAndSundayEndpointsInMarketTime(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        bool expected)
    {
        FxMarketSchedule schedule = CreateSchedule();

        bool result = schedule.IsOpen(Utc(year, month, day, hour, minute, second));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToMarketTime_Handles2026SpringDaylightSavingTransition()
    {
        FxMarketSchedule schedule = CreateSchedule();

        DateTimeOffset before = schedule.ToMarketTime(Utc(2026, 3, 8, 6, 59, 59));
        DateTimeOffset after = schedule.ToMarketTime(Utc(2026, 3, 8, 7, 0, 0));

        Assert.Equal(1, before.Hour);
        Assert.Equal(59, before.Minute);
        Assert.Equal(59, before.Second);
        Assert.Equal(TimeSpan.FromHours(-5), before.Offset);
        Assert.Equal(3, after.Hour);
        Assert.Equal(0, after.Minute);
        Assert.Equal(TimeSpan.FromHours(-4), after.Offset);
    }

    [Fact]
    public void ToMarketTime_Handles2026FallDaylightSavingTransition()
    {
        FxMarketSchedule schedule = CreateSchedule();

        DateTimeOffset before = schedule.ToMarketTime(Utc(2026, 11, 1, 5, 59, 59));
        DateTimeOffset after = schedule.ToMarketTime(Utc(2026, 11, 1, 6, 0, 0));

        Assert.Equal(1, before.Hour);
        Assert.Equal(59, before.Minute);
        Assert.Equal(59, before.Second);
        Assert.Equal(TimeSpan.FromHours(-4), before.Offset);
        Assert.Equal(1, after.Hour);
        Assert.Equal(0, after.Minute);
        Assert.Equal(TimeSpan.FromHours(-5), after.Offset);
    }

    [Theory]
    [InlineData(2026, 3, 8, 20, 59, false)]
    [InlineData(2026, 3, 8, 21, 0, true)]
    [InlineData(2026, 11, 1, 21, 59, false)]
    [InlineData(2026, 11, 1, 22, 0, true)]
    public void SundayOpenUtcBoundary_Tracks2026DaylightSavingOffset(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        bool expected)
    {
        FxMarketSchedule schedule = CreateSchedule();

        bool result = schedule.IsOpen(Utc(year, month, day, hour, minute, 0));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(2026, 7, 8, 3, 29, false)]
    [InlineData(2026, 7, 8, 3, 30, true)]
    [InlineData(2026, 7, 8, 8, 59, true)]
    [InlineData(2026, 7, 8, 9, 0, false)]
    public void IsSleepWindow_UsesInclusiveStartExclusiveEndAcrossMidnight(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        bool expected)
    {
        FxMarketSchedule schedule = CreateSchedule();

        bool result = schedule.IsSleepWindow(Utc(year, month, day, hour, minute, 0));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void TradingDayOpen_WithEveningRollover_UsesPreviousOpeningBeforeRollover()
    {
        FxMarketSchedule schedule = new(Options.Create(new ForexAlertOptions
        {
            MarketTimeZone = "America/New_York",
            SundayOpenTime = TimeSpan.FromHours(17),
            TradingDayOpenTime = TimeSpan.FromHours(17),
        }));

        DateTimeOffset beforeMondayRollover = schedule.GetTradingDayOpenUtc(Utc(2026, 7, 13, 14, 0, 0));
        DateTimeOffset afterMondayRollover = schedule.GetTradingDayOpenUtc(Utc(2026, 7, 13, 22, 0, 0));

        Assert.Equal(Utc(2026, 7, 12, 21, 0, 0), beforeMondayRollover);
        Assert.Equal(Utc(2026, 7, 13, 21, 0, 0), afterMondayRollover);
    }

    private static FxMarketSchedule CreateSchedule() =>
        new(Options.Create(new ForexAlertOptions
        {
            MarketTimeZone = "America/New_York",
            FridayCloseTime = TimeSpan.FromHours(17),
            SundayOpenTime = TimeSpan.FromHours(17),
            SleepWindowStart = new TimeSpan(23, 30, 0),
            SleepWindowEnd = TimeSpan.FromHours(5),
        }));

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);
}
