using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using AqiClock.Domain.Time;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace AqiClock.Application.Services;

public sealed partial class NotificationScheduler : INotificationScheduler,
    IRecipient<ClockTick>, IRecipient<TimeJumped>, IRecipient<DataChanged>, IRecipient<SessionChanged>, IDisposable
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(120);
    private readonly ITimetableRepository _timetables;
    private readonly IWeekScheduleRepository _weekSchedule;
    private readonly IDateOverrideRepository _overrides;
    private readonly IAnnouncementRepository _announcements;
    private readonly IClassRepository _classes;
    private readonly IDeviceAudienceContext _audience;
    private readonly INotificationLogStore _log;
    private readonly INotificationPresenter _presenter;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;
    private readonly ILogger<NotificationScheduler> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ScheduleSnapshot _snapshot = ScheduleSnapshot.Empty;
    private IReadOnlyList<NotificationEvent> _pending = [];
    private DateOnly? _pendingDate;
    private Dictionary<string, DateTime> _knownTriggers = new(StringComparer.Ordinal);
    private bool _disposed;

    public NotificationScheduler(
        ITimetableRepository timetables,
        IWeekScheduleRepository weekSchedule,
        IDateOverrideRepository overrides,
        IAnnouncementRepository announcements,
        INotificationLogStore log,
        INotificationPresenter presenter,
        ISettingsService settings,
        IClock clock,
        IMessenger messenger,
        ILogger<NotificationScheduler> logger)
        : this(timetables, weekSchedule, overrides, announcements, new EmptyClassRepository(), new DeviceAudienceContext(), log, presenter, settings, clock, messenger, logger)
    {
    }

    public NotificationScheduler(
        ITimetableRepository timetables,
        IWeekScheduleRepository weekSchedule,
        IDateOverrideRepository overrides,
        IAnnouncementRepository announcements,
        IClassRepository classes,
        IDeviceAudienceContext audience,
        INotificationLogStore log,
        INotificationPresenter presenter,
        ISettingsService settings,
        IClock clock,
        IMessenger messenger,
        ILogger<NotificationScheduler> logger)
    {
        _timetables = timetables;
        _weekSchedule = weekSchedule;
        _overrides = overrides;
        _announcements = announcements;
        _classes = classes;
        _audience = audience;
        _log = log;
        _presenter = presenter;
        _settings = settings;
        _clock = clock;
        _logger = logger;
        messenger.RegisterAll(this);
        settings.Changed += OnSettingsChanged;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _log.PruneAsync(DateTimeOffset.UtcNow.AddDays(-7), cancellationToken).ConfigureAwait(false);
        await RebuildAsync(_clock.Now, cancellationToken).ConfigureAwait(false);
        await ProcessAnnouncementsAsync(_clock.Now, cancellationToken).ConfigureAwait(false);
    }

    public async Task RebuildAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _snapshot = new ScheduleSnapshot(
                await _timetables.GetAllAsync(cancellationToken).ConfigureAwait(false),
                await _weekSchedule.GetAsync(cancellationToken).ConfigureAwait(false),
                await _overrides.GetAllAsync(cancellationToken).ConfigureAwait(false));

            TimeSpan lead = TimeSpan.FromMinutes(Math.Clamp(_settings.Current.EndWarningMinutes, 0, 15));
            IReadOnlyList<NotificationEvent> rebuilt = ScheduleEngine.GetNotificationEvents(_snapshot, DateOnly.FromDateTime(now), lead);
            foreach (NotificationEvent item in rebuilt)
            {
                if (_knownTriggers.TryGetValue(item.Key, out DateTime oldTrigger) && oldTrigger <= now && item.TriggerTime > now && oldTrigger != item.TriggerTime)
                    await _log.RemoveAsync(item.Key, cancellationToken).ConfigureAwait(false);
            }

            _knownTriggers = rebuilt.ToDictionary(item => item.Key, item => item.TriggerTime, StringComparer.Ordinal);
            _pending = rebuilt;
            _pendingDate = DateOnly.FromDateTime(now);
        }
        finally { _gate.Release(); }
    }

    public async Task ProcessAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateOnly today = DateOnly.FromDateTime(now);
            if (_pendingDate != today)
            {
                TimeSpan lead = TimeSpan.FromMinutes(Math.Clamp(_settings.Current.EndWarningMinutes, 0, 15));
                _pending = ScheduleEngine.GetNotificationEvents(_snapshot, today, lead);
                _knownTriggers = _pending.ToDictionary(item => item.Key, item => item.TriggerTime, StringComparer.Ordinal);
                _pendingDate = today;
            }

            foreach (NotificationEvent item in _pending.Where(item => item.TriggerTime <= now).ToArray())
            {
                if (await _log.ContainsAsync(item.Key, cancellationToken).ConfigureAwait(false)) continue;
                if (now - item.TriggerTime > Grace || !IsEnabled(item.Kind))
                {
                    await _log.RecordAsync(item.Key, new DateTimeOffset(now), true, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!await AppliesToDeviceAsync(item.Occurrence.Period, cancellationToken).ConfigureAwait(false))
                {
                    await _log.RecordAsync(item.Key, new DateTimeOffset(now), true, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (item.Kind == NotificationEventKind.EndWarning && await SuppressEndWarningAsync(item, now, cancellationToken).ConfigureAwait(false))
                {
                    await _log.RecordAsync(item.Key, new DateTimeOffset(now), true, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (item.Kind == NotificationEventKind.LessonStart)
                {
                    EffectiveDay day = ScheduleEngine.ResolveDay(_snapshot, item.Occurrence.Date);
                    int ordinal = Array.FindIndex(day.Periods.ToArray(), period => period.Id == item.Occurrence.Period.Id) + 1;
                    await _presenter.ShowLessonStartAsync(item, ordinal, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    LessonStatus status = ScheduleEngine.GetStatus(_snapshot, item.TriggerTime);
                    await _presenter.ShowEndWarningAsync(item, status.Next, _settings.Current.EndWarningMinutes, cancellationToken).ConfigureAwait(false);
                }

                await _log.RecordAsync(item.Key, new DateTimeOffset(now), false, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }

    public void Receive(ClockTick message) => RunSafely(async () =>
    {
        await ProcessAsync(message.Now).ConfigureAwait(false);
        await ProcessAnnouncementsAsync(message.Now).ConfigureAwait(false);
    });
    public void Receive(TimeJumped message) => RunSafely(async () => { await RebuildAsync(message.Current).ConfigureAwait(false); await ProcessAsync(message.Current).ConfigureAwait(false); });
    public void Receive(DataChanged message)
    {
        if (message.Table is CacheTable.Timetables or CacheTable.Periods or CacheTable.WeekSchedule or CacheTable.DateOverrides)
            RunSafely(() => RebuildAsync(_clock.Now));
        if (message.Table is CacheTable.Announcements)
            RunSafely(() => ProcessAnnouncementsAsync(_clock.Now));
    }
    public void Receive(SessionChanged message)
    {
        if (message.State.UserId is not null) RunSafely(() => RebuildAsync(_clock.Now));
    }

    private bool IsEnabled(NotificationEventKind kind) => kind == NotificationEventKind.LessonStart
        ? _settings.Current.LessonStartNotifications
        : _settings.Current.EndWarningNotifications;

    private async Task<bool> SuppressEndWarningAsync(NotificationEvent item, DateTime now, CancellationToken cancellationToken)
    {
        if (item.Occurrence.Period.Duration <= TimeSpan.FromMinutes(_settings.Current.EndWarningMinutes)) return true;
        string startKey = item.Key.Replace("end-warning:", "start:", StringComparison.Ordinal);
        NotificationLogEntry? start = await _log.GetAsync(startKey, cancellationToken).ConfigureAwait(false);
        return start is { Skipped: false, FiredAt: { } firedAt } && new DateTimeOffset(now) - firedAt < TimeSpan.FromSeconds(60);
    }

    private async Task ProcessAnnouncementsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Announcement> items = await _announcements.GetCurrentAsync(new DateTimeOffset(now), cancellationToken).ConfigureAwait(false);
        DateTimeOffset instant = new(now);
        foreach (Announcement item in items.Where(item => item.IsPublishedAt(instant) && !item.IsExpiredAt(instant) && _audience.Matches(item)))
        {
            string key = $"announcement:{item.Id:N}";
            if (await _log.ContainsAsync(key, cancellationToken).ConfigureAwait(false)) continue;
            if (_settings.Current.AnnouncementNotifications)
                await _presenter.ShowAnnouncementAsync(item, cancellationToken).ConfigureAwait(false);
            await _log.RecordAsync(key, new DateTimeOffset(now), !_settings.Current.AnnouncementNotifications, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> AppliesToDeviceAsync(Period period, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid> periodClasses = await _classes.GetClassIdsForPeriodAsync(period.Id, cancellationToken).ConfigureAwait(false);
        return _audience.MatchesPeriod(periodClasses, period.StartTime);
    }

    private void OnSettingsChanged(object? sender, SettingsChanged args) => RunSafely(() => RebuildAsync(_clock.Now));

    private void RunSafely(Func<Task> operation) => _ = ExecuteAsync(operation);
    private async Task ExecuteAsync(Func<Task> operation)
    {
        try { await operation().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { LogBackgroundOperationFailed(_logger, exception); }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification scheduler background operation failed")]
    private static partial void LogBackgroundOperationFailed(ILogger logger, Exception exception);

    public void Dispose()
    {
        if (_disposed) return;
        _settings.Changed -= OnSettingsChanged;
        _gate.Dispose();
        _disposed = true;
    }
}
