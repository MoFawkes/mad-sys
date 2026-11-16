using System.Diagnostics;
using System.Globalization;
using AqiClock.Application.Abstractions;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using AqiClock.Domain.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace AqiClock.App.Services;

public sealed partial class ToastPresenter : INotificationPresenter, IDisposable
{
    private readonly IWindowService _windows;
    private readonly ILogger<ToastPresenter> _logger;
    private readonly IClock? _clock;
    private bool _disposed;

    public ToastPresenter(IWindowService windows, ILogger<ToastPresenter> logger, IClock? clock = null)
    {
        _windows = windows; _logger = logger; _clock = clock;
        ToastNotificationManagerCompat.OnActivated += OnActivated;
    }

    public Task ShowLessonStartAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
        => ShowLessonStartsAsync([notification], cancellationToken);

    public Task ShowLessonStartsAsync(IReadOnlyList<NotificationEvent> notifications, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ToastContentBuilder toast = new ToastContentBuilder()
            .AddArgument("action", "open")
            .AddText(notifications[0].Kind == NotificationEventKind.NaseehahStart
                ? "Naseehah starting" : notifications.Count == 1 ? "Lesson starting" : "Lessons starting");
        foreach (NotificationEvent notification in notifications)
            toast.AddText(string.Create(CultureInfo.CurrentCulture, $"{notification.Occurrence.Period.Name} · ends {notification.Occurrence.Period.EndTime:HH:mm}{InstituteSuffix()}"));
        return SubmitAsync(toast, notifications[0].Kind.ToString(), cancellationToken);
    }

    public Task ShowEndWarningAsync(NotificationEvent notification, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default)
        => ShowEndWarningsAsync([notification], followingPeriod, warningMinutes, cancellationToken);

    public Task ShowEndWarningsAsync(IReadOnlyList<NotificationEvent> notifications, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ToastContentBuilder toast = new ToastContentBuilder()
            .AddArgument("action", "open")
            .AddText(string.Create(CultureInfo.CurrentCulture, $"{(notifications.Count == 1 ? "Lesson ends" : "Lessons end")} in {warningMinutes} minutes"));
        foreach (NotificationEvent notification in notifications)
            toast.AddText(string.Create(CultureInfo.CurrentCulture, $"{notification.Occurrence.Period.Name} · {notification.Occurrence.Period.EndTime:HH:mm}{InstituteSuffix()}"));
        if (followingPeriod is not null)
            toast.AddText($"Next: {followingPeriod.Period.Name}");
        return SubmitAsync(toast, nameof(NotificationEventKind.EndWarning), cancellationToken);
    }

    public Task ShowAnnouncementAsync(Announcement announcement, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string body = announcement.Body.Length <= 100 ? announcement.Body : announcement.Body[..100] + "…";
        return SubmitAsync(new ToastContentBuilder().AddArgument("action", "announcement").AddText(announcement.Title).AddText(body), "Announcement", cancellationToken);
    }

    public Task ShowTestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SubmitAsync(new ToastContentBuilder().AddArgument("action", "open").AddText("AQI Clock test notification").AddText("This is a test notification."), "Test", cancellationToken);
    }

    public Task ShowReminderAsync(RecurringReminder reminder, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SubmitAsync(new ToastContentBuilder().AddArgument("action", "open").AddText(reminder.Title).AddText(reminder.Body), "Reminder", cancellationToken);
    }

    // Windows submits to the notification service synchronously. Keep that call off the UI thread.
    private Task SubmitAsync(ToastContentBuilder toast, string kind, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            LogSubmissionStarted(_logger, kind);
            long started = Stopwatch.GetTimestamp();
            toast.Show();
            if (_logger.IsEnabled(LogLevel.Debug))
                LogSubmissionReturned(_logger, kind, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }, cancellationToken);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Submitting {Kind} notification to Windows")]
    private static partial void LogSubmissionStarted(ILogger logger, string kind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Windows notification call for {Kind} returned after {ElapsedMilliseconds:F0} ms")]
    private static partial void LogSubmissionReturned(ILogger logger, string kind, double elapsedMilliseconds);

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

    private string InstituteSuffix() => _clock is IInstituteClock institute && institute.DiffersFromDeviceZone ? " (institute time)" : string.Empty;
}
