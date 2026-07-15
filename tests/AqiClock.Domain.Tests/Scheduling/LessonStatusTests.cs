using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using static AqiClock.Domain.Tests.Scheduling.TestData;

namespace AqiClock.Domain.Tests.Scheduling;

public sealed class LessonStatusTests
{
    [Fact]
    public void StatusDuringALessonReportsRemainingAndProgress()
    {
        LessonStatus status = ScheduleEngine.GetStatus(WeekOf(NormalDay()), At(Monday, "09:45"));

        Assert.Equal("Period 1", status.Current!.Period.Name);
        Assert.Equal("Break", status.Next!.Period.Name);
        Assert.Equal(TimeSpan.FromMinutes(15), status.TimeRemaining);
        Assert.Equal(0.75, status.Progress!.Value, precision: 10);
    }

    [Fact]
    public void StatusOutsideLessonsHasNullRemainingAndProgress()
    {
        LessonStatus status = ScheduleEngine.GetStatus(WeekOf(NormalDay()), At(Monday, "07:00"));

        Assert.Null(status.Current);
        Assert.Null(status.TimeRemaining);
        Assert.Null(status.Progress);
        Assert.Equal("Period 1", status.Next!.Period.Name);
    }

    [Fact]
    public void RemainingClampsAtZeroAndProgressAtOne()
    {
        // Construct directly: a status whose timestamp has drifted past the period end
        // (e.g. between engine recomputations) must never show negative remaining time.
        var occurrence = new PeriodOccurrence(Monday, P("Period 1", "09:00", "10:00"));
        var day = new EffectiveDay(Monday, null, EffectiveDaySource.WeekSchedule, [occurrence.Period]);
        var status = new LessonStatus(At(Monday, "10:00:30"), day, occurrence, Next: null);

        Assert.Equal(TimeSpan.Zero, status.TimeRemaining);
        Assert.Equal(1d, status.Progress);
    }

    [Fact]
    public void ClosedDayStatusPointsAtTheNextSchoolDay()
    {
        ScheduleSnapshot snapshot = WeekOf(NormalDay(),
            new DateOverride(Guid.NewGuid(), Monday, TimetableId: null, Note: "Eid"));

        LessonStatus status = ScheduleEngine.GetStatus(snapshot, At(Monday, "09:30"));

        Assert.False(status.Day.IsSchoolDay);
        Assert.Null(status.Current);
        Assert.Equal(Tuesday, status.Next!.Date);
    }

    [Fact]
    public void DstTransitionDatesUseWallClockArithmeticOnly()
    {
        // Europe/London springs forward on 2026-03-29 (01:00 → 02:00). The engine is
        // timezone-agnostic by design (ADR-006): remaining time is the wall-clock
        // difference, so a lesson spanning the skipped hour reads consistently.
        DateOnly springForward = new(2026, 3, 29); // Sunday
        Timetable timetable = Tt("Early", P("Fajr class", "00:30", "03:30"));
        var snapshot = new ScheduleSnapshot(
            [timetable],
            new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Sunday] = timetable.Id }),
            []);

        LessonStatus status = ScheduleEngine.GetStatus(snapshot, At(springForward, "03:00"));

        Assert.Equal("Fajr class", status.Current!.Period.Name);
        Assert.Equal(TimeSpan.FromMinutes(30), status.TimeRemaining);
    }

    [Fact]
    public void MidDayEditIsAPureRecomputationOnTheNewSnapshot()
    {
        // Simulates ARCHITECTURE.md §4 "timetable edited mid-day": same instant, new
        // snapshot without the current period → the engine reports no current lesson.
        DateTime now = At(Monday, "09:30");
        Timetable before = NormalDay();
        Timetable after = Tt("Edited Day", P("Period 2", "10:20", "11:20"));

        Assert.Equal("Period 1", ScheduleEngine.GetStatus(WeekOf(before), now).Current!.Period.Name);
        Assert.Null(ScheduleEngine.GetStatus(WeekOf(after), now).Current);
        Assert.Equal("Period 2", ScheduleEngine.GetStatus(WeekOf(after), now).Next!.Period.Name);
    }
}
