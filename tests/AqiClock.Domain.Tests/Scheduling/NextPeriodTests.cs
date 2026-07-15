using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using static AqiClock.Domain.Tests.Scheduling.TestData;

namespace AqiClock.Domain.Tests.Scheduling;

public sealed class NextPeriodTests
{
    [Fact]
    public void BeforeFirstPeriodNextIsTheFirstPeriodToday()
    {
        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(WeekOf(NormalDay()), At(Monday, "07:30"));

        Assert.Equal("Period 1", next!.Period.Name);
        Assert.Equal(Monday, next.Date);
    }

    [Fact]
    public void DuringAPeriodNextIsTheFollowingPeriod()
    {
        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(WeekOf(NormalDay()), At(Monday, "09:30"));

        Assert.Equal("Break", next!.Period.Name);
    }

    [Fact]
    public void PeriodStartingExactlyNowIsCurrentNotNext()
    {
        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(WeekOf(NormalDay()), At(Monday, "09:00"));

        Assert.Equal("Break", next!.Period.Name);
    }

    [Fact]
    public void AfterLastPeriodNextComesFromTheNextSchoolDay()
    {
        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(WeekOf(NormalDay()), At(Monday, "15:00"));

        Assert.Equal(Tuesday, next!.Date);
        Assert.Equal("Period 1", next.Period.Name);
    }

    [Fact]
    public void FridayEveningScansAcrossTheWeekendToMonday()
    {
        DateOnly friday = new(2026, 7, 17);
        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(WeekOf(NormalDay()), At(friday, "18:00"));

        Assert.Equal(new DateOnly(2026, 7, 20), next!.Date);
    }

    [Fact]
    public void ScanSkipsClosedOverrideDays()
    {
        ScheduleSnapshot snapshot = WeekOf(NormalDay(),
            new DateOverride(Guid.NewGuid(), Tuesday, TimetableId: null, Note: "Holiday"));

        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(snapshot, At(Monday, "15:00"));

        Assert.Equal(new DateOnly(2026, 7, 15), next!.Date);
    }

    [Fact]
    public void ScanSkipsDaysWhoseTimetableHasNoValidPeriods()
    {
        Timetable normal = NormalDay();
        Timetable broken = Tt("Broken", P("Inverted", "11:00", "09:00"));
        var snapshot = new ScheduleSnapshot(
            [normal, broken],
            new WeekSchedule(new Dictionary<DayOfWeek, Guid?>
            {
                [DayOfWeek.Monday] = normal.Id,
                [DayOfWeek.Tuesday] = broken.Id,
                [DayOfWeek.Wednesday] = normal.Id,
            }),
            []);

        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(snapshot, At(Monday, "15:00"));

        Assert.Equal(new DateOnly(2026, 7, 15), next!.Date);
    }

    [Fact]
    public void NextUsesTheOverrideTimetableOnAFutureDate()
    {
        Timetable normal = NormalDay();
        Timetable exam = Tt("Exam Day", P("Exam", "08:30", "12:00"));
        var snapshot = new ScheduleSnapshot(
            [normal, exam],
            new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Monday] = normal.Id }),
            [new DateOverride(Guid.NewGuid(), Tuesday, exam.Id)]);

        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(snapshot, At(Monday, "15:00"));

        Assert.Equal(Tuesday, next!.Date);
        Assert.Equal("Exam", next.Period.Name);
    }

    [Fact]
    public void EmptyScheduleHasNoNextPeriod() =>
        Assert.Null(ScheduleEngine.FindNextPeriod(ScheduleSnapshot.Empty, At(Monday, "09:00")));

    [Fact]
    public void LessonExactlyAtTheLookaheadLimitIsFound()
    {
        Timetable normal = NormalDay();
        DateOnly farDate = Monday.AddDays(ScheduleEngine.LookaheadDays);
        var snapshot = new ScheduleSnapshot(
            [normal],
            WeekSchedule.Empty,
            [new DateOverride(Guid.NewGuid(), farDate, normal.Id)]);

        PeriodOccurrence? next = ScheduleEngine.FindNextPeriod(snapshot, At(Monday, "09:00"));

        Assert.Equal(farDate, next!.Date);
    }

    [Fact]
    public void LessonBeyondTheLookaheadLimitIsNotFound()
    {
        Timetable normal = NormalDay();
        DateOnly tooFar = Monday.AddDays(ScheduleEngine.LookaheadDays + 1);
        var snapshot = new ScheduleSnapshot(
            [normal],
            WeekSchedule.Empty,
            [new DateOverride(Guid.NewGuid(), tooFar, normal.Id)]);

        Assert.Null(ScheduleEngine.FindNextPeriod(snapshot, At(Monday, "09:00")));
    }
}
