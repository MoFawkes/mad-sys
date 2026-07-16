using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;

namespace AqiClock.Application.Abstractions;

public interface INotificationPresenter
{
    Task ShowLessonStartAsync(NotificationEvent notification, int periodNumber, CancellationToken cancellationToken = default);
    Task ShowEndWarningAsync(NotificationEvent notification, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default);
    Task ShowAnnouncementAsync(Announcement announcement, CancellationToken cancellationToken = default);
    Task ShowTestAsync(CancellationToken cancellationToken = default);
}

public interface INotificationScheduler
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task RebuildAsync(DateTime now, CancellationToken cancellationToken = default);
    Task ProcessAsync(DateTime now, CancellationToken cancellationToken = default);
}
