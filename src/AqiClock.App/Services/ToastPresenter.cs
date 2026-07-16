using System.Globalization;
using System.Windows;
using AqiClock.Application.Abstractions;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using Microsoft.Toolkit.Uwp.Notifications;

namespace AqiClock.App.Services;

public sealed class ToastPresenter : INotificationPresenter, IDisposable
{
    private readonly IWindowService _windows;
    private bool _disposed;

    public ToastPresenter(IWindowService windows)
    {
        _windows = windows;
        ToastNotificationManagerCompat.OnActivated += OnActivated;
    }

    public Task ShowLessonStartAsync(NotificationEvent notification, int periodNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new ToastContentBuilder()
            .AddArgument("action", "open")
            .AddText(string.Create(CultureInfo.CurrentCulture, $"Period {periodNumber} — {notification.Occurrence.Period.Name}"))
            .AddText(string.Create(CultureInfo.CurrentCulture, $"Started now · ends {notification.Occurrence.Period.EndTime:HH:mm}"))
            .Show();
        return Task.CompletedTask;
    }

    public Task ShowEndWarningAsync(NotificationEvent notification, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string nextText = followingPeriod is null ? string.Empty : $" · next: {followingPeriod.Period.Name}";
        new ToastContentBuilder()
            .AddArgument("action", "open")
            .AddText(string.Create(CultureInfo.CurrentCulture, $"{warningMinutes} minutes left"))
            .AddText(string.Create(CultureInfo.CurrentCulture, $"{notification.Occurrence.Period.Name} ends at {notification.Occurrence.Period.EndTime:HH:mm}{nextText}"))
            .Show();
        return Task.CompletedTask;
    }

    public Task ShowAnnouncementAsync(Announcement announcement, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string body = announcement.Body.Length <= 100 ? announcement.Body : announcement.Body[..100] + "…";
        new ToastContentBuilder().AddArgument("action", "announcement").AddText(announcement.Title).AddText(body).Show();
        return Task.CompletedTask;
    }

    public Task ShowTestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new ToastContentBuilder().AddArgument("action", "open").AddText("AQI Clock notifications are working").AddText("This is a test notification.").Show();
        return Task.CompletedTask;
    }

    private void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            ToastArguments arguments = ToastArguments.Parse(args.Argument);
            if (arguments.TryGetValue("action", out string? action) && string.Equals(action, "announcement", StringComparison.Ordinal))
                _windows.ShowAnnouncements();
            else
                _windows.ActivateMainWindow();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        ToastNotificationManagerCompat.OnActivated -= OnActivated;
        _disposed = true;
    }
}
