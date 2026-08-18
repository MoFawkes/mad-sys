namespace AqiClock.Domain.Entities;

public sealed record WeekScheduleEntry(Guid Id, DayOfWeek Weekday, Guid? AudienceClassId, Guid? TimetableId);

public sealed class WeekSchedule
{
    public static WeekSchedule Empty { get; } = new(Array.Empty<WeekScheduleEntry>());
    private readonly WeekScheduleEntry[] _entries;

    public WeekSchedule(IEnumerable<WeekScheduleEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToArray();
    }

    public WeekSchedule(IReadOnlyDictionary<DayOfWeek, Guid?> defaults)
        : this(defaults.Select(item => new WeekScheduleEntry(Guid.Empty, item.Key, null, item.Value))) { }

    public IReadOnlyList<WeekScheduleEntry> AllEntries => _entries;
    public IReadOnlyList<WeekScheduleEntry> EntriesFor(DayOfWeek weekday) =>
        _entries.Where(entry => entry.Weekday == weekday).ToArray();

    public WeekScheduleEntry? ResolveFor(DayOfWeek weekday, IReadOnlySet<Guid> audienceClassIds)
    {
        IReadOnlyList<WeekScheduleEntry> entries = ResolveAllFor(weekday, audienceClassIds);
        return entries.Count == 0 ? null : entries[0];
    }

    public IReadOnlyList<WeekScheduleEntry> ResolveAllFor(DayOfWeek weekday, IReadOnlySet<Guid> audienceClassIds)
    {
        ArgumentNullException.ThrowIfNull(audienceClassIds);
        WeekScheduleEntry[] matches = _entries
            .Where(entry => entry.Weekday == weekday && entry.AudienceClassId is { } classId && audienceClassIds.Contains(classId))
            .OrderBy(entry => entry.AudienceClassId!.Value.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (matches.Length > 0) return matches;
        WeekScheduleEntry? fallback = _entries.FirstOrDefault(entry => entry.Weekday == weekday && entry.AudienceClassId is null);
        return fallback is null ? [] : [fallback];
    }

    public Guid? TimetableIdFor(DayOfWeek weekday) => ResolveFor(weekday, new HashSet<Guid>())?.TimetableId;
}
