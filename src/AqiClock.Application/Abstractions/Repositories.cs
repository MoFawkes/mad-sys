using AqiClock.Domain.Entities;

namespace AqiClock.Application.Abstractions;

public interface ITimetableRepository
{
    Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default);
}

public interface IAnnouncementRepository
{
    Task<IReadOnlyList<Announcement>> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public interface IWeekScheduleRepository
{
    Task<WeekSchedule> GetAsync(CancellationToken cancellationToken = default);
}

public interface IDateOverrideRepository
{
    Task<IReadOnlyList<DateOverride>> GetAllAsync(CancellationToken cancellationToken = default);
}

public interface IProfileRepository
{
    Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface INotificationLogStore
{
    Task<bool> ContainsAsync(string eventKey, CancellationToken cancellationToken = default);
    Task<NotificationLogEntry?> GetAsync(string eventKey, CancellationToken cancellationToken = default);
    Task RecordAsync(string eventKey, DateTimeOffset? firedAt, bool skipped, CancellationToken cancellationToken = default);
    Task RemoveAsync(string eventKey, CancellationToken cancellationToken = default);
    Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}

public sealed record NotificationLogEntry(string EventKey, DateTimeOffset? FiredAt, bool Skipped);

public interface IAnnouncementReadStore
{
    Task<bool> IsReadAsync(Guid announcementId, CancellationToken cancellationToken = default);
    Task MarkReadAsync(Guid announcementId, DateTimeOffset readAt, CancellationToken cancellationToken = default);
}
