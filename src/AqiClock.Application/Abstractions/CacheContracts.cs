using System.Text.Json.Serialization;

namespace AqiClock.Application.Abstractions;

public enum CacheTable
{
    Organizations,
    Profiles,
    Timetables,
    Periods,
    Classes,
    PeriodClasses,
    WeekSchedule,
    DateOverrides,
    Announcements,
}

public sealed record OrganizationRow(Guid Id, string Name, string Timezone);
public sealed record ProfileRow(Guid Id, [property: JsonPropertyName("org_id")] Guid OrganizationId, string DisplayName, string Role, bool IsActive);
public sealed record TimetableRow(Guid Id, [property: JsonPropertyName("org_id")] Guid OrganizationId, string Name, bool IsArchived);
public sealed record PeriodRow(Guid Id, Guid TimetableId, string Name, TimeOnly StartTime, TimeOnly EndTime, int SortOrder, bool IsLesson);
public sealed record ClassRow(Guid Id, [property: JsonPropertyName("org_id")] Guid OrganizationId, string Name, int SortOrder);
public sealed record PeriodClassRow(Guid PeriodId, Guid ClassId);
public sealed record WeekScheduleRow(Guid Id, [property: JsonPropertyName("org_id")] Guid OrganizationId, int Weekday, Guid? TimetableId);
public sealed record DateOverrideRow(Guid Id, [property: JsonPropertyName("org_id")] Guid OrganizationId, DateOnly Date, Guid? TimetableId, string? Note);
public sealed record AnnouncementRow(Guid Id, [property: JsonPropertyName("org_id")] Guid OrganizationId, string Title, string Body, DateTimeOffset? ExpiresAt, Guid CreatedBy, DateTimeOffset CreatedAt, string AudienceType = "everyone", Guid? AudienceClassId = null, string UpdateType = "general", DateTimeOffset? PublishAt = null, string? EMasjidLink = null, string Status = "published", DateTimeOffset? DeletedAt = null);

public sealed record CacheSnapshot(CacheTable Table, IReadOnlyList<object> Rows, DateTimeOffset SyncedAt);

public interface ILocalCache
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task WipeAsync(CancellationToken cancellationToken = default);
    Task ReplaceSnapshotAsync(CacheSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<string?> GetMetaAsync(string key, CancellationToken cancellationToken = default);
    Task SetMetaAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<DateTimeOffset?> GetLastSyncedAtAsync(CacheTable table, CancellationToken cancellationToken = default);
}
