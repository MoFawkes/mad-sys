namespace AqiClock.Domain.Scheduling;

public static class AlQalamExpansionRules
{
    public const int PrayerMinutes = 10;
    public const int NaseehahMinutes = 15;

    public static GeneratorResult Expand(Guid timetableId, GeneratorSessionKind kind, TimeOnly start,
        IReadOnlyList<GeneratorBlock> blocks, IReadOnlyList<ResolvedAnchor> anchors, TimeOnly? advisoryEnd = null,
        string namingPattern = "Lesson {number}") =>
        TimetableGenerator.Expand(timetableId, kind, start, blocks, anchors, advisoryEnd, PrayerMinutes, NaseehahMinutes, namingPattern);
}
