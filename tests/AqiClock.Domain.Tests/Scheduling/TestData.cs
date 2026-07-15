using System.Globalization;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;

namespace AqiClock.Domain.Tests.Scheduling;

/// <summary>Builders for concise test setup. Monday 2026-07-13 is the reference week.</summary>
internal static class TestData
{
    public static readonly DateOnly Monday = new(2026, 7, 13);
    public static readonly DateOnly Tuesday = new(2026, 7, 14);

    public static Period P(string name, string start, string end, int sort = 0, bool isLesson = true, Guid? id = null) =>
        new(id ?? Guid.NewGuid(),
            name,
            TimeOnly.Parse(start, CultureInfo.InvariantCulture),
            TimeOnly.Parse(end, CultureInfo.InvariantCulture),
            sort,
            isLesson);

    public static Timetable Tt(string name, params Period[] periods) =>
        new(Guid.NewGuid(), name, IsArchived: false, periods);

    /// <summary>A snapshot where every weekday (Mon–Fri) uses <paramref name="timetable"/>.</summary>
    public static ScheduleSnapshot WeekOf(Timetable timetable, params DateOverride[] overrides) =>
        new(
            [timetable],
            new WeekSchedule(new Dictionary<DayOfWeek, Guid?>
            {
                [DayOfWeek.Monday] = timetable.Id,
                [DayOfWeek.Tuesday] = timetable.Id,
                [DayOfWeek.Wednesday] = timetable.Id,
                [DayOfWeek.Thursday] = timetable.Id,
                [DayOfWeek.Friday] = timetable.Id,
            }),
            overrides);

    /// <summary>A standard three-period day: 09:00–10:00, 10:00–10:20 break, 10:20–11:20.</summary>
    public static Timetable NormalDay() =>
        Tt("Normal Day",
            P("Period 1", "09:00", "10:00", sort: 1),
            P("Break", "10:00", "10:20", sort: 2, isLesson: false),
            P("Period 2", "10:20", "11:20", sort: 3));

    public static DateTime At(DateOnly date, string time) =>
        date.ToDateTime(TimeOnly.Parse(time, CultureInfo.InvariantCulture));
}
