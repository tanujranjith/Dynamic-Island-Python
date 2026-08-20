namespace DynamicIsland.Windows.Models;

public sealed class RecurrenceRule
{
    public AlarmRepeat Repeat { get; set; } = AlarmRepeat.Once;
    public int WeekdayMask { get; set; }
    public int IntervalDays { get; set; } = 1;
    public DateTime? EndDate { get; set; }
}

public static class RecurrenceCalculator
{
    public static DateTimeOffset? Next(
        DateTimeOffset after,
        int hour,
        int minute,
        AlarmRepeat repeat,
        int? anchorDayOfWeek = null,
        DateTime? anchorDate = null,
        int weekdayMask = 0,
        int intervalDays = 1,
        DateTime? endDate = null)
    {
        if (repeat == AlarmRepeat.Once)
        {
            var once = Candidate(after, hour, minute, 0);
            if (once <= after) once = once.AddDays(1);
            return IsBeforeEnd(once, endDate) ? once : null;
        }

        var startDate = anchorDate?.Date ?? after.Date;
        var candidate = Candidate(after, hour, minute, 0);
        for (var offset = 0; offset <= 3660; offset++)
        {
            var date = candidate.Date.AddDays(offset);
            if (endDate is not null && date.Date > endDate.Value.Date) return null;
            var matches = repeat switch
            {
                AlarmRepeat.Daily => true,
                AlarmRepeat.Weekdays => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
                AlarmRepeat.Weekends => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                AlarmRepeat.Weekly => anchorDayOfWeek is null || (int)date.DayOfWeek == anchorDayOfWeek.Value,
                AlarmRepeat.SelectedWeekdays => weekdayMask != 0 && (weekdayMask & (1 << (int)date.DayOfWeek)) != 0,
                AlarmRepeat.EveryNDays => (date.Date - startDate).Days >= 0 &&
                    (date.Date - startDate).Days % Math.Max(1, intervalDays) == 0,
                _ => false
            };
            if (!matches) continue;
            var value = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, after.Offset);
            if (value > after && IsBeforeEnd(value, endDate)) return value;
        }
        return null;
    }

    private static DateTimeOffset Candidate(DateTimeOffset after, int hour, int minute, int dayOffset) =>
        new DateTimeOffset(after.Year, after.Month, after.Day, Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59), 0, after.Offset).AddDays(dayOffset);

    private static bool IsBeforeEnd(DateTimeOffset value, DateTime? endDate) =>
        endDate is null || value.Date <= endDate.Value.Date;
}
