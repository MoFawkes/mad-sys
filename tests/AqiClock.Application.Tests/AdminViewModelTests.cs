using AqiClock.App.ViewModels;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.Application.Tests;

public sealed class AdminViewModelTests
{
    [Fact]
    public async Task TimetableValidationBlocksInvalidAndDuplicateNamesButOnlyWarnsOverlap()
    {
        var messenger = new WeakReferenceMessenger();
        var timetable = new Timetable(Guid.NewGuid(), "Normal", false, []);
        var vm = Editor(messenger, timetable);
        await vm.LoadAsync(); vm.Selected = timetable;
        vm.Periods.Add(new() { Id = Guid.NewGuid(), Name = "Maths", Start = new(10, 0, 0), End = new(9, 0, 0) });
        Assert.False(vm.Validate()); Assert.Contains("end after", vm.ValidationMessage, StringComparison.Ordinal);

        vm.Periods[0].End = new(11, 0, 0);
        vm.Periods.Add(new() { Id = Guid.NewGuid(), Name = "maths", Start = new(10, 30, 0), End = new(11, 30, 0) });
        Assert.False(vm.Validate()); Assert.Contains("unique", vm.ValidationMessage, StringComparison.Ordinal);

        vm.Periods[1].Name = "English";
        Assert.True(vm.Validate()); Assert.Contains("overlap", vm.WarningMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirtyTimetableShowsCourtesyConflictOnRemoteChange()
    {
        var messenger = new WeakReferenceMessenger(); var timetable = new Timetable(Guid.NewGuid(), "Normal", false, []); var vm = Editor(messenger, timetable);
        await vm.LoadAsync(); vm.Selected = timetable; vm.Name = "Changed locally";
        messenger.Send(new DataChanged(CacheTable.Timetables));
        Assert.True(vm.HasConflict);
    }

    [Fact]
    public void ExpiryPresetsProduceExpectedBoundaries()
    {
        DateTimeOffset now = new(2026, 7, 16, 12, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 23, 59, 59, 999, now.Offset).AddTicks(9999), AnnouncementComposeViewModel.ResolveExpiry(ExpiryPreset.EndOfDay, null, now));
        Assert.Null(AnnouncementComposeViewModel.ResolveExpiry(ExpiryPreset.Never, null, now));
        Assert.Equal(new DateTime(2026, 7, 20, 10, 0, 0), AnnouncementComposeViewModel.ResolveExpiry(ExpiryPreset.Custom, new DateTime(2026, 7, 20, 10, 0, 0), now)?.DateTime);
    }

    [Fact]
    public async Task LastAdminErrorIsMappedToFriendlyUserMessage()
    {
        var gateway = new Gateway { ProfileFailure = new LastAdminException("guard") };
        var users = new UsersViewModel(new Profiles(new Profile(Guid.NewGuid(), "Admin", UserRole.Admin, true)), gateway, new Sync(), new Session(), new Windows());
        await users.LoadAsync(); UserEditorItem item = Assert.Single(users.Items); item.Role = UserRole.Staff;
        await users.SaveCommand.ExecuteAsync(item);
        Assert.Contains("last active admin", item.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveDemotionClosesAdminWindow()
    {
        var messenger = new WeakReferenceMessenger(); var windows = new Windows(); Gateway gateway = new(); Sync sync = new(); Timetables timetables = new(); Week week = new(); Overrides overrides = new(); Profiles profiles = new(); Session session = new();
        var admin = new AdminViewModel(new(gateway, sync, timetables, week, overrides, windows, messenger), new(week, timetables, gateway, sync, windows), new(overrides, timetables, gateway, sync, windows), new(gateway, sync, session, new Announcements(), windows), new(gateway, profiles, sync), new(profiles, gateway, sync, session, windows), sync, windows, messenger);
        messenger.Send(new SessionChanged(new SessionState(Guid.NewGuid(), "staff@example.test", UserRole.Staff, true, false)));
        Assert.True(windows.AdminClosed);
    }

    private static TimetableEditorViewModel Editor(IMessenger messenger, params Timetable[] rows) => new(new Gateway(), new Sync(), new Timetables(rows), new Week(), new Overrides(), new Windows(), messenger);
    private sealed class Timetables(params Timetable[] rows) : ITimetableRepository { public Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Timetable>>(rows); }
    private sealed class Week : IWeekScheduleRepository { public Task<WeekSchedule> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(WeekSchedule.Empty); }
    private sealed class Overrides(params DateOverride[] rows) : IDateOverrideRepository { public Task<IReadOnlyList<DateOverride>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DateOverride>>(rows); }
    private sealed class Announcements(params Announcement[] rows) : IAnnouncementRepository { public Task<IReadOnlyList<Announcement>> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Announcement>>(rows); }
    private sealed class Profiles(params Profile[] rows) : IProfileRepository { public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Profile>>(rows); public Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(rows.FirstOrDefault(x => x.Id == id)); }
    private sealed class Session : ISessionService { public SessionState Current { get; } = new(Guid.NewGuid(), "admin@example.test", UserRole.Admin, true, false); public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SignInAsync(string email, string password, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class Sync : ISyncService { public ConnectivityState State { get; set; } = ConnectivityState.Online; public DateTimeOffset? LastSyncedAt => DateTimeOffset.UtcNow; public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SyncAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SyncTableAsync(CacheTable table, CancellationToken cancellationToken = default) => Task.CompletedTask; public void SignalTableChanged(CacheTable table) { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class Windows : IWindowService { public bool AdminClosed { get; private set; } public void ShowMainWindow() { } public void ShowSignInWindow() { } public void ShowSettingsWindow() { } public void ShowAdminWindow() { } public void CloseAdminWindow() => AdminClosed = true; public void ShowAnnouncements() { } public void HideMainWindow() { } public void ActivateMainWindow() { } public void CloseSignInWindow() { } public void ShutdownApplication() { } public void ExitApplication() { } }
    private sealed class Gateway : ISupabaseGateway
    {
        public Exception? ProfileFailure { get; init; }
        public Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException(); public Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException(); public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid()); public Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default) => throw new NotSupportedException(); public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default) => ProfileFailure is null ? Task.CompletedTask : Task.FromException(ProfileFailure); public Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEntry>>([]); public Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
