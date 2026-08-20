using DynamicIsland.Windows.Models;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class RecurrenceTests
{
    [Fact]
    public void OnceAlarmMovesToTomorrowWhenTodaysTimeHasPassed()
    {
        var after = new DateTimeOffset(2026, 8, 9, 18, 0, 0, TimeSpan.FromHours(-4));
        var next = RecurrenceCalculator.Next(after, 7, 30, AlarmRepeat.Once);

        Assert.Equal(new DateTimeOffset(2026, 8, 10, 7, 30, 0, TimeSpan.FromHours(-4)), next);
    }

    [Fact]
    public void SelectedWeekdaysUsesTheConfiguredMask()
    {
        var after = new DateTimeOffset(2026, 8, 9, 8, 0, 0, TimeSpan.FromHours(-4)); // Sunday
        var mask = 1 << (int)DayOfWeek.Monday;
        var next = RecurrenceCalculator.Next(after, 9, 0, AlarmRepeat.SelectedWeekdays, weekdayMask: mask);

        Assert.Equal(DayOfWeek.Monday, next?.DayOfWeek);
    }

    [Fact]
    public void IntervalRecurrenceHonoursEndDate()
    {
        var after = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var next = RecurrenceCalculator.Next(after, 9, 0, AlarmRepeat.EveryNDays,
            anchorDate: new DateTime(2026, 8, 9), intervalDays: 2, endDate: new DateTime(2026, 8, 10));

        Assert.Null(next);
    }
}
