using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;

namespace AqiClock.Application.Abstractions;

public interface INotificationPresenter
{
    Task ShowLessonStartAsync(NotificationEvent notification, CancellationToken cancellationToken = default);
    async Task ShowLessonStartsAsync(IReadOnlyList<NotificationEvent> notifications, CancellationToken cancellationToken = default)
    {
        foreach (NotificationEvent notification in notifications)
            await ShowLessonStartAsync(notification, cancellationToken).ConfigureAwait(false);
    }
    Task ShowEndWarningAsync(NotificationEvent notification, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default);
    Task ShowEndWarningsAsync(IReadOnlyList<NotificationEvent> notifications, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default)
        => ShowEndWarningAsync(notifications[0], followingPeriod, warningMinutes, cancellationToken);
    Task ShowAnnouncementAsync(Announcement announcement, CancellationToken cancellationToken = default);
    Task ShowTestAsync(CancellationToken cancellationToken = default);
}

public interface INotificationScheduler
{
    string HealthSummary { get; }
    event EventHandler? HealthChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task RebuildAsync(DateTime now, CancellationToken cancellationToken = default);
    Task ProcessAsync(DateTime now, CancellationToken cancellationToken = default);
}
