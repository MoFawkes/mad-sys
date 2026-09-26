using AqiClock.Domain.Scheduling;
using System.Globalization;

namespace AqiClock.Domain.Tests;

public sealed class TimetableGeneratorTests
{
    private static readonly Guid TimetableId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void R6Pm25August2026MatchesEveryMinute()
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId, new(new(18, 15), 5, 25, null, null),
            [new(Guid.Parse("30000000-0000-0000-0000-000000000001"), "asr", "Asr", new(18, 40), 10),
             new(Guid.Parse("30000000-0000-0000-0000-000000000002"), "maghrib", "Maghrib", new(20, 12), 10)]);

        Assert.Collection(result.Periods,
            p => At(p, "Lesson 1", "18:15", "18:40"),
            p => At(p, "Asr + Naseehah", "18:40", "19:05"),
            p => At(p, "Lesson 2", "19:05", "19:30"),
            p => At(p, "Lesson 3", "19:30", "19:55"),
            p => At(p, "Lesson 4 (part 1)", "19:55", "20:12"),
            p => At(p, "Maghrib", "20:12", "20:22"),
            p => At(p, "Lesson 4 (part 2)", "20:22", "20:30"),
            p => At(p, "Lesson 5", "20:30", "20:55"));
    }

    [Fact]
    public void R6AmMondayToThursdayFinishesBeforeZuhr()
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId, new(new(9, 10), 8, 30, 4, 25),
            [new(Guid.NewGuid(), "zuhr", "Zuhr", new(13, 37), 10)]);

        Assert.Equal(9, result.Periods.Count);
        At(result.Periods[3], "Lesson 4", "10:40", "11:10");
        At(result.Periods[4], "Break / Naseehah", "11:10", "11:35");
        At(result.Periods[8], "Lesson 8", "13:05", "13:35");
        Assert.DoesNotContain(result.Periods, period => period.Name == "Zuhr");
    }

    [Fact]
    public void MissingApplicableAnchorDurationRefusesExpansion()
    {
        Assert.Throws<InvalidOperationException>(() => TimetableGenerator.Expand(TimetableId, new(new(9, 10), 8, 30, null, null),
            [new(Guid.NewGuid(), "zuhr", "Zuhr", new(12, 58), null)]));
    }

    [Fact]
    public void LateIshaAppliesAgainstBumpedSessionEnd()
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId, new(new(18, 15), 5, 25, null, null),
            [new(Guid.NewGuid(), "asr", "Asr", new(18, 40), 10),
             new(Guid.NewGuid(), "maghrib", "Maghrib", new(19, 30), 10),
             new(Guid.NewGuid(), "isha", "Isha", new(20, 30), 10)]);

        GeneratedPeriod isha = Assert.Single(result.Periods, period => period.Name == "Isha");
        At(isha, "Isha", "20:30", "20:40");
        Assert.Equal(new TimeOnly(21, 5), result.Periods[^1].End);
    }

    [Fact]
    public void FridayUsesResolvedRowAndSplitsLessonSevenWithoutFridayLogic()
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId, new(new(9, 10), 8, 30, 4, 25),
            [new(Guid.NewGuid(), "zuhr", "Zuhr", new(12, 58), 30)]);

        At(result.Periods[7], "Lesson 7 (part 1)", "12:35", "12:58");
        At(result.Periods[8], "Zuhr", "12:58", "13:28");
        At(result.Periods[9], "Lesson 7 (part 2)", "13:28", "13:35");
        At(result.Periods[10], "Lesson 8", "13:35", "14:05");
    }

    [Fact]
    public void MovingAnchorPreservesIdsOfUntouchedLessons()
    {
        var shape = new TimetableShape(new(18, 15), 5, 25, null, null);
        Guid anchorId = Guid.NewGuid();
        GeneratorResult first = TimetableGenerator.Expand(TimetableId, shape,
            [new(anchorId, "maghrib", "Maghrib", new(19, 32), 10)]);
        GeneratorResult moved = TimetableGenerator.Expand(TimetableId, shape,
            [new(anchorId, "maghrib", "Maghrib", new(19, 35), 10)]);

        foreach (string untouched in (string[])["Lesson 1", "Lesson 2", "Lesson 3", "Lesson 5"])
            Assert.Equal(
                Assert.Single(first.Periods, period => period.Name == untouched).Id,
                Assert.Single(moved.Periods, period => period.Name == untouched).Id);
    }

    [Fact]
    public void PmWithoutApplicableAnchorWarnsAndDoesNotInventSlot()
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId, new(new(18, 15), 1, 25, null, null),
            [new(Guid.NewGuid(), "isha", "Isha", new(20, 30), 10)]);
        Assert.Equal("naseehah-unplaced", Assert.Single(result.Warnings).Code);
        Assert.DoesNotContain(result.Periods, period => !period.IsLesson);
    }

    [Fact]
    public void SessionDerivationChangesAtFifteenHundred()
    {
        Guid timetableId = Guid.NewGuid();
        var anchor = new ResolvedAnchor(Guid.NewGuid(), "asr", "Asr", new(15, 10), 10);

        GeneratorResult morning = TimetableGenerator.Expand(timetableId,
            new(new(14, 59), 1, 30, null, null, true), [anchor]);
        GeneratorResult evening = TimetableGenerator.Expand(timetableId,
            new(new(15, 0), 1, 30, null, null, true), [anchor]);

        Assert.Contains(morning.Periods, period => period.Name == "Asr"
            && period.End - period.Start == TimeSpan.FromMinutes(TimetableGenerator.PrayerMinutes));
        Assert.Contains(evening.Periods, period => period.Name == "Asr + Naseehah"
            && period.End - period.Start == TimeSpan.FromMinutes(
                TimetableGenerator.PrayerMinutes + TimetableGenerator.NaseehahMinutes));
    }

    [Fact]
    public void PrayerAdjustmentOffProducesPlainScheduleWithoutNaseehahWarning()
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId,
            new(new(18, 15), 3, 25, null, null, AdjustsForPrayer: false),
            [new(Guid.NewGuid(), "asr", "Asr", new(18, 40), 10)]);
        Assert.Equal(["Lesson 1", "Lesson 2", "Lesson 3"], result.Periods.Select(period => period.Name));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void LongAnchorPreservesTeachingAcrossTwoOriginalLessonSlots()
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId, new(new(9, 0), 3, 30, null, null),
            [new(Guid.NewGuid(), "zuhr", "Zuhr", new(9, 15), 70)]);
        Assert.Equal(90, result.Periods.Where(period => period.IsLesson)
            .Sum(period => (int)(period.End - period.Start).TotalMinutes));
        Assert.Equal(new TimeOnly(11, 40), result.Periods[^1].End);
    }

    [Fact]
    public void MorningBreakHostsNaseehahOnlyWhenPrayerAdjustmentIsOn()
    {
        GeneratorResult adjusted = TimetableGenerator.Expand(TimetableId, new(new(9, 0), 2, 30, 1, 10), []);
        GeneratorResult plain = TimetableGenerator.Expand(TimetableId, new(new(9, 0), 2, 30, 1, 10, false), []);
        Assert.Equal("Break / Naseehah", adjusted.Periods[1].Name);
        Assert.Equal("Break", plain.Periods[1].Name);
    }

    [Fact]
    public void MarginalNaseehahHostInputTerminatesWithBaselineHost()
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId, new(new(18, 15), 4, 15, null, null),
            [new(Guid.NewGuid(), "x", "X", new(18, 20), 10),
             new(Guid.NewGuid(), "z", "Z", new(19, 35), 10)]);

        Assert.Contains(result.Periods, period => period.Name == "X + Naseehah");
        Assert.Contains(result.Periods, period => period.Name == "Z");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void BreakCanAppearAtEitherSupportedBoundary(int afterLesson)
    {
        GeneratorResult result = TimetableGenerator.Expand(TimetableId,
            new(new(9, 0), 8, 30, afterLesson, 10, AdjustsForPrayer: false), []);
        Assert.Equal("Break", result.Periods[afterLesson].Name);
        Assert.Equal(9, result.Periods.Count);
        Assert.Equal("Lesson 8", result.Periods[^1].Name);
    }

    [Fact]
    public void InvalidShapeIsRejectedBeforeExpansion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimetableGenerator.Expand(TimetableId, new(new(9, 0), 21, 30, null, null), []));
        Assert.Throws<ArgumentException>(() =>
            TimetableGenerator.Expand(TimetableId, new(new(9, 0), 8, 30, 4, null), []));
    }

    private static void At(GeneratedPeriod period, string name, string start, string end)
    {
        Assert.Equal(name, period.Name);
        Assert.Equal(TimeOnly.Parse(start, CultureInfo.InvariantCulture), period.Start);
        Assert.Equal(TimeOnly.Parse(end, CultureInfo.InvariantCulture), period.End);
    }
}
