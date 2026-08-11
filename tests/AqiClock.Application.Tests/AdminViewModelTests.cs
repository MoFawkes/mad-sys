using AqiClock.App.ViewModels;
using AqiClock.App.Views;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.Messaging;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AqiClock.Application.Tests;

public sealed class AdminViewModelTests
{
    [Fact]
    public async Task MovingPeriodThenSaveSendsSingleAtomicPayloadInVisualOrder()
    {
        Period first = new(Guid.NewGuid(), "First", new(9, 0), new(10, 0), 0);
        Period second = new(Guid.NewGuid(), "Second", new(10, 0), new(11, 0), 1);
        var timetable = new Timetable(Guid.NewGuid(), "Day", false, [first, second]);
        var gateway = new Gateway();
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable), new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync();

        vm.MoveUpCommand.Execute(vm.Periods[1]);
        await vm.SaveCommand.ExecuteAsync(null);

        IReadOnlyList<PeriodRow> saved = Assert.IsAssignableFrom<IReadOnlyList<PeriodRow>>(gateway.SavedPeriods);
        Assert.Equal([second.Id, first.Id], saved.Select(x => x.Id));
        Assert.Equal([0, 1], saved.Select(x => x.SortOrder));
    }

    [Fact]
    public async Task RemovingMiddlePeriodThenSaveSendsOnlyContiguousSurvivors()
    {
        Period[] periods = Enumerable.Range(0, 3).Select(i => new Period(Guid.NewGuid(), $"P{i}", new(9 + i, 0), new(10 + i, 0), i)).ToArray();
        var timetable = new Timetable(Guid.NewGuid(), "Day", false, periods);
        var gateway = new Gateway();
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable), new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync();

        vm.RemovePeriodCommand.Execute(vm.Periods[1]);
        await vm.SaveCommand.ExecuteAsync(null);

        IReadOnlyList<PeriodRow> saved = Assert.IsAssignableFrom<IReadOnlyList<PeriodRow>>(gateway.SavedPeriods);
        Assert.Equal([periods[0].Id, periods[2].Id], saved.Select(x => x.Id));
        Assert.Equal([0, 1], saved.Select(x => x.SortOrder));
    }

    [Fact]
    public async Task AddingPeriodMidListThenSaveUsesItsVisualIndex()
    {
        Period first = new(Guid.NewGuid(), "First", new(9, 0), new(10, 0), 0);
        Period last = new(Guid.NewGuid(), "Last", new(11, 0), new(12, 0), 1);
        var timetable = new Timetable(Guid.NewGuid(), "Day", false, [first, last]);
        var gateway = new Gateway();
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable), new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync();
        var middle = new PeriodEditorItem { Id = Guid.NewGuid(), Name = "Middle", Start = new(10, 0, 0), End = new(11, 0, 0) };

        vm.Periods.Insert(1, middle);
        await vm.SaveCommand.ExecuteAsync(null);

        IReadOnlyList<PeriodRow> saved = Assert.IsAssignableFrom<IReadOnlyList<PeriodRow>>(gateway.SavedPeriods);
        Assert.Equal([first.Id, middle.Id, last.Id], saved.Select(x => x.Id));
        Assert.Equal([0, 1, 2], saved.Select(x => x.SortOrder));
    }

    [Fact]
    public async Task TeacherSessionDoesNotCountAsEnrolledStudentDevice()
    {
        var cache = new Cache();
        var audience = new DeviceAudienceContext(new WeakReferenceMessenger(), cache);
        var viewModel = new StudentClassPickerViewModel(new Classes(), cache, audience, new Session(), new Sync());

        await viewModel.LoadAsync();

        Assert.False(viewModel.IsEnrolled);
        Assert.True(viewModel.NeedsEnrollment);
    }

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
    public async Task ReloadClearsLatchedConflictAndOwnSaveEchoDoesNotCreateOne()
    {
        var messenger = new WeakReferenceMessenger();
        var timetable = new Timetable(Guid.NewGuid(), "Normal", false, []);
        var sync = new EchoSync(messenger);
        var vm = new TimetableEditorViewModel(new Gateway(), sync, new Timetables(timetable), new Week(), new Overrides(), new Windows(), messenger);
        await vm.LoadAsync();
        vm.Name = "Dirty";
        messenger.Send(new DataChanged(CacheTable.Timetables));
        Assert.True(vm.HasConflict);

        await vm.ReloadCommand.ExecuteAsync(null);
        Assert.False(vm.HasConflict);
        Assert.False(vm.IsDirty);

        vm.Name = "Saved";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.HasConflict);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void AdminWindowBindingsRenderAndCommitEditableSelectionsWithoutErrors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var errors = new List<string>();
            var listener = new CaptureListener(errors);
            try
            {
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                Guid timetableId = Guid.NewGuid();
                Guid currentId = Guid.NewGuid();
                var timetable = new Timetable(timetableId, "Normal Day", false, []);
                var profiles = new Profiles(new Profile(currentId, "Current Admin", UserRole.Admin, true), new Profile(Guid.NewGuid(), "Teacher Member", UserRole.Teacher, true));
                var session = new Session(currentId);
                var messenger = new WeakReferenceMessenger();
                var gateway = new Gateway(); var sync = new Sync(); var windows = new Windows(); var timetables = new Timetables(timetable); var week = new Week();
                var overrides = new Overrides(new DateOverride(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), null, null));
                var admin = new AdminViewModel(new(gateway, sync, timetables, week, overrides, windows, messenger), new(week, timetables, gateway, sync, windows), new(overrides, timetables, gateway, sync, windows), new(gateway, sync, session, new Announcements(), windows), new(gateway, profiles, sync), new(profiles, gateway, sync, session, windows), sync, windows, messenger);
                var window = new AdminWindow(admin, new Settings(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AqiClock.App.Services.WindowPlacementController>.Instance);
                WpfUiTestResources.Attach(window);
                window.Show();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var tabs = FindVisualChild<TabControl>(window) ?? throw new InvalidOperationException("Admin tabs did not render.");

                tabs.SelectedIndex = 1; window.UpdateLayout();
                ComboBox weekCombo = FindVisualChild<ComboBox>((DependencyObject)tabs.SelectedContent) ?? throw new InvalidOperationException("Week selector did not render.");
                weekCombo.SelectedIndex = 0; window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.Equal(timetableId, admin.WeekSchedule.Rows[0].TimetableId);

                tabs.SelectedIndex = 2; window.UpdateLayout();
                ComboBox overrideCombo = FindVisualChild<ComboBox>((DependencyObject)tabs.SelectedContent) ?? throw new InvalidOperationException("Override selector did not render.");
                overrideCombo.SelectedIndex = 0; window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.Equal(timetableId, admin.Overrides.Items[0].TimetableId);

                tabs.SelectedIndex = 7; window.UpdateLayout();
                DataGrid users = FindVisualChild<DataGrid>((DependencyObject)tabs.SelectedContent) ?? throw new InvalidOperationException("Users grid did not render.");
                UserEditorItem current = admin.Users.Items[0]; users.ScrollIntoView(current); window.UpdateLayout();
                Assert.Equal("Current Admin", ((TextBlock?)users.Columns[0].GetCellContent(current))?.Text);
                Assert.Equal("admin@example.test", ((TextBlock?)users.Columns[1].GetCellContent(current))?.Text);
                Assert.Equal(UserRole.Admin, current.Role);
                Assert.Equal(UserRole.Admin, FindVisualChild<ComboBox>(users)?.SelectedItem);
                window.Close();
                Assert.Empty(errors);
            }
            catch (Exception exception) { failure = exception; }
            finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
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
        await users.LoadAsync(); UserEditorItem item = Assert.Single(users.Items, x => x.IsEditable); item.Role = UserRole.Teacher;
        await users.SaveCommand.ExecuteAsync(item);
        Assert.Contains("last active admin", item.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveDemotionShowsRoleChangedState()
    {
        var messenger = new WeakReferenceMessenger(); var windows = new Windows(); Gateway gateway = new(); Sync sync = new(); Timetables timetables = new(); Week week = new(); Overrides overrides = new(); Profiles profiles = new(); Session session = new();
        var admin = new AdminViewModel(new(gateway, sync, timetables, week, overrides, windows, messenger), new(week, timetables, gateway, sync, windows), new(overrides, timetables, gateway, sync, windows), new(gateway, sync, session, new Announcements(), windows), new(gateway, profiles, sync), new(profiles, gateway, sync, session, windows), sync, windows, messenger);
        messenger.Send(new SessionChanged(new SessionState(Guid.NewGuid(), "teacher@example.test", UserRole.Teacher, true, false, RoleConfirmed: true)));
        Assert.Contains("role changed", admin.Banner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SyncingKeepsAdminEditableAndRoleBannerSurvivesOnline()
    {
        var messenger = new WeakReferenceMessenger(); var windows = new Windows(); Gateway gateway = new(); Sync sync = new(); Timetables timetables = new(); Week week = new(); Overrides overrides = new(); Profiles profiles = new(); Session session = new();
        var admin = new AdminViewModel(new(gateway, sync, timetables, week, overrides, windows, messenger), new(week, timetables, gateway, sync, windows), new(overrides, timetables, gateway, sync, windows), new(gateway, sync, session, new Announcements(), windows), new(gateway, profiles, sync), new(profiles, gateway, sync, session, windows), sync, windows, messenger);

        messenger.Send(new ConnectivityChanged(ConnectivityState.Syncing, null));
        Assert.True(admin.IsEditable);
        Assert.Null(admin.Banner);
        messenger.Send(new SessionChanged(new SessionState(Guid.NewGuid(), "teacher@example.test", UserRole.Teacher, true, false, RoleConfirmed: true)));
        messenger.Send(new ConnectivityChanged(ConnectivityState.Online, DateTimeOffset.UtcNow));

        Assert.Contains("role changed", admin.Banner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProvisionalRoleAndSignedOutDoNotShowRoleChangedState()
    {
        var messenger = new WeakReferenceMessenger(); var windows = new Windows(); Gateway gateway = new(); Sync sync = new(); Timetables timetables = new(); Week week = new(); Overrides overrides = new(); Profiles profiles = new(); Session session = new();
        var admin = new AdminViewModel(new(gateway, sync, timetables, week, overrides, windows, messenger), new(week, timetables, gateway, sync, windows), new(overrides, timetables, gateway, sync, windows), new(gateway, sync, session, new Announcements(), windows), new(gateway, profiles, sync), new(profiles, gateway, sync, session, windows), sync, windows, messenger);

        messenger.Send(new SessionChanged(new SessionState(Guid.NewGuid(), "admin@example.test", UserRole.Teacher, true, false)));
        messenger.Send(new SessionChanged(SessionState.SignedOut));

        Assert.Null(admin.Banner);
    }

    [Fact]
    public void ConfirmedAdminClearsExistingRoleChangedState()
    {
        var messenger = new WeakReferenceMessenger(); var windows = new Windows(); Gateway gateway = new(); Sync sync = new(); Timetables timetables = new(); Week week = new(); Overrides overrides = new(); Profiles profiles = new(); Session session = new();
        var admin = new AdminViewModel(new(gateway, sync, timetables, week, overrides, windows, messenger), new(week, timetables, gateway, sync, windows), new(overrides, timetables, gateway, sync, windows), new(gateway, sync, session, new Announcements(), windows), new(gateway, profiles, sync), new(profiles, gateway, sync, session, windows), sync, windows, messenger);
        messenger.Send(new SessionChanged(new SessionState(Guid.NewGuid(), "teacher@example.test", UserRole.Teacher, true, false, RoleConfirmed: true)));

        messenger.Send(new SessionChanged(new SessionState(Guid.NewGuid(), "admin@example.test", UserRole.Admin, true, false, RoleConfirmed: true)));

        Assert.Null(admin.Banner);
    }

    [Fact]
    public async Task TimetableDeleteCancelDoesNotWrite()
    {
        var messenger = new WeakReferenceMessenger();
        var timetable = new Timetable(Guid.NewGuid(), "Disposable", false, []);
        var gateway = new Gateway();
        var windows = new Windows(confirmResult: false);
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable), new Week(), new Overrides(), windows, messenger);
        await vm.LoadAsync();

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(0, gateway.DeleteCalls);
    }

    [Fact]
    public async Task TimetableUsedOnlyByTrackReportsQualifiedUsageBeforeDelete()
    {
        Guid timetableId = Guid.NewGuid(), classId = Guid.NewGuid();
        var timetable = new Timetable(timetableId, "Part-Time", false, []);
        var week = new Week(new WeekScheduleEntry(Guid.NewGuid(), DayOfWeek.Monday, classId, timetableId));
        var gateway = new Gateway();
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable), week, new Overrides(), new Classes(new AqiClock.Domain.Entities.Class(classId, "Part-Time", 0)), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync();

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Contains("Monday (Part-Time)", vm.ValidationMessage, StringComparison.Ordinal);
        Assert.Equal(0, gateway.DeleteCalls);
    }

    [Fact]
    public async Task WeekScheduleAddSaveAndDeleteCommandsUseAudienceRpcContract()
    {
        Guid classId = Guid.NewGuid(), timetableId = Guid.NewGuid();
        var gateway = new Gateway();
        var vm = new WeekScheduleViewModel(new Week(), new Timetables(new Timetable(timetableId, "Part-Time", false, [])), gateway, new Sync(), new Windows(), new Classes(new AqiClock.Domain.Entities.Class(classId, "Part-Time", 0)));
        await vm.LoadAsync();
        WeekScheduleItem monday = vm.Rows.Single(row => row.Weekday == 0);

        vm.AddRowCommand.Execute(monday);
        WeekScheduleItem added = vm.Rows.Single(row => row.Weekday == 0 && row.IsNew);
        added.AudienceClassId = classId;
        added.TimetableId = timetableId;
        await vm.SaveRowCommand.ExecuteAsync(added);

        Assert.Equal(0, gateway.SavedWeekday);
        Assert.Equal(classId, gateway.SavedAudienceClassId);
        Assert.Equal(timetableId, gateway.SavedWeekTimetableId);

        vm.AddRowCommand.Execute(monday);
        WeekScheduleItem unsaved = vm.Rows.Last(row => row.Weekday == 0 && row.IsNew);
        Assert.True(unsaved.CanDelete);
        await vm.DeleteRowCommand.ExecuteAsync(unsaved);
        Assert.DoesNotContain(unsaved, vm.Rows);
    }

    [Fact]
    public async Task WeekScheduleDeletePersistedTrackUsesDeleteRpc()
    {
        Guid classId = Guid.NewGuid();
        var gateway = new Gateway();
        var vm = new WeekScheduleViewModel(new Week(new WeekScheduleEntry(Guid.NewGuid(), DayOfWeek.Monday, classId, null)), new Timetables(), gateway, new Sync(), new Windows(), new Classes(new AqiClock.Domain.Entities.Class(classId, "Part-Time", 0)));
        await vm.LoadAsync();

        await vm.DeleteRowCommand.ExecuteAsync(vm.Rows.Single(row => row.AudienceClassId == classId));

        Assert.Equal(0, gateway.DeletedWeekday);
        Assert.Equal(classId, gateway.DeletedAudienceClassId);
    }

    [Fact]
    public async Task OverrideDeleteCancelDoesNotWrite()
    {
        DateOverride value = new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), null, "test");
        var gateway = new Gateway();
        var vm = new OverridesViewModel(new Overrides(value), new Timetables(), gateway, new Sync(), new Windows(confirmResult: false));
        await vm.LoadAsync();

        await vm.DeleteCommand.ExecuteAsync(vm.Items[0]);

        Assert.Equal(0, gateway.DeleteCalls);
    }

    [Fact]
    public async Task UserRoleOrActivationCancelDoesNotWrite()
    {
        var gateway = new Gateway();
        var users = new UsersViewModel(new Profiles(new Profile(Guid.NewGuid(), "Admin", UserRole.Admin, true)), gateway, new Sync(), new Session(), new Windows(confirmResult: false));
        await users.LoadAsync();
        UserEditorItem item = Assert.Single(users.Items, x => x.IsEditable);
        item.Role = UserRole.Teacher;
        item.IsActive = false;

        await users.SaveCommand.ExecuteAsync(item);

        Assert.Equal(0, gateway.ProfileUpdateCalls);
    }

    [Fact]
    public async Task SoftDeletePreservesPublishTimeAndDeletedHistoryCannotBeRepublished()
    {
        DateTimeOffset publishAt = new(2026, 7, 20, 14, 30, 0, TimeSpan.Zero);
        Announcement active = new(Guid.NewGuid(), "Notice", "Body", publishAt.AddDays(-1), Guid.NewGuid(), null, PublishAt: publishAt);
        var gateway = new Gateway();
        var vm = new AnnouncementComposeViewModel(gateway, new Sync(), new Session(), new Announcements(active), new Windows());

        await vm.DeleteCommand.ExecuteAsync(active);

        AnnouncementRow deleted = Assert.IsType<AnnouncementRow>(gateway.LastUpdatedRow);
        Assert.Equal(publishAt, deleted.PublishAt);
        Assert.NotNull(deleted.DeletedAt);

        Announcement historyItem = active with { DeletedAt = DateTimeOffset.Now };
        int updates = gateway.UpdateCalls;
        await vm.PublishItemCommand.ExecuteAsync(historyItem);
        Assert.Equal(updates, gateway.UpdateCalls);
        Assert.Contains("cannot be republished", vm.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GraduateAudienceIsReservedButNotOfferedForComposition()
    {
        var vm = new AnnouncementComposeViewModel(new Gateway(), new Sync(), new Session(), new Announcements(), new Windows());
        Assert.DoesNotContain(AudienceType.Graduates, vm.Audiences);
    }

    [Fact]
    public async Task ScheduledPublishCombinesSelectedDateAndTime()
    {
        var gateway = new Gateway();
        var vm = new AnnouncementComposeViewModel(gateway, new Sync(), new Session(), new Announcements(), new Windows())
        {
            Title = "Scheduled",
            Body = "Body",
            PublishAt = new DateTime(2030, 8, 12),
            PublishTime = "14:35",
        };

        await vm.PublishCommand.ExecuteAsync(null);

        AnnouncementRow row = Assert.IsType<AnnouncementRow>(gateway.LastInsertedRow);
        Assert.Equal(new DateTime(2030, 8, 12, 14, 35, 0), row.PublishAt?.LocalDateTime);
        Assert.True(row.ExpiresAt > row.PublishAt);
        Assert.Equal(new DateTime(2030, 8, 12, 23, 59, 59, 999).AddTicks(9999), row.ExpiresAt?.LocalDateTime);
    }

    [Fact]
    public async Task ScheduledPublishRejectsExpiryBeforePublication()
    {
        var gateway = new Gateway();
        var vm = new AnnouncementComposeViewModel(gateway, new Sync(), new Session(), new Announcements(), new Windows())
        {
            Title = "Scheduled",
            Body = "Body",
            PublishAt = new DateTime(2030, 8, 12),
            PublishTime = "14:35",
            Expiry = ExpiryPreset.Custom,
            CustomExpiry = new DateTime(2030, 8, 12, 10, 0, 0),
        };

        await vm.PublishCommand.ExecuteAsync(null);

        Assert.Null(gateway.LastInsertedRow);
        Assert.Contains("later than the publication", vm.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClassAddUsesNextAvailableSortOrderAndConstraintErrorsAreFriendly()
    {
        var classes = new Classes(new AqiClock.Domain.Entities.Class(Guid.NewGuid(), "A", 0), new AqiClock.Domain.Entities.Class(Guid.NewGuid(), "C", 2));
        var gateway = new Gateway();
        var vm = new ClassesViewModel(classes, gateway, new Sync());
        await vm.LoadAsync();

        vm.AddCommand.Execute(null);
        Assert.Equal(3, vm.Items[^1].SortOrder);

        gateway.WriteFailure = new DuplicateRowException("duplicate");
        await vm.SaveCommand.ExecuteAsync(vm.Items[^1]);
        Assert.Contains("name or sort order", vm.Error, StringComparison.OrdinalIgnoreCase);

        gateway.WriteFailure = null;
        gateway.DeleteFailure = new ReferencedRowException("referenced");
        await vm.DeleteCommand.ExecuteAsync(vm.Items[0]);
        Assert.Contains("referenced by an announcement", vm.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InProgressCellEditSurvivesRemoteChangeInsteadOfBeingDiscarded()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Period period = new(Guid.NewGuid(), "Lesson 1", new(9, 10), new(9, 40), 0);
                var timetable = new Timetable(Guid.NewGuid(), "Normal Day", false, [period]);
                var messenger = new WeakReferenceMessenger();
                var gateway = new Gateway(); var sync = new Sync(); var windows = new Windows();
                var timetables = new Timetables(timetable); var week = new Week(); var overrides = new Overrides();
                var profiles = new Profiles();
                var editor = new TimetableEditorViewModel(gateway, sync, timetables, week, overrides, windows, messenger);
                var admin = new AdminViewModel(editor, new(week, timetables, gateway, sync, windows), new(overrides, timetables, gateway, sync, windows), new(gateway, sync, new Session(Guid.NewGuid()), new Announcements(), windows), new(gateway, profiles, sync), new(profiles, gateway, sync, new Session(Guid.NewGuid()), windows), sync, windows, messenger);
                admin.InitializeAsync().GetAwaiter().GetResult();

                var window = new AdminWindow(admin, new Settings(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AqiClock.App.Services.WindowPlacementController>.Instance);
                WpfUiTestResources.Attach(window);
                window.Show();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var tabs = FindVisualChild<TabControl>(window) ?? throw new InvalidOperationException("Admin tabs did not render.");
                tabs.SelectedIndex = 0; window.UpdateLayout();
                DataGrid grid = FindVisualChild<DataGrid>((DependencyObject)tabs.SelectedContent) ?? throw new InvalidOperationException("Periods grid did not render.");

                PeriodEditorItem item = editor.Periods[0];
                grid.ScrollIntoView(item);
                grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[1]);   // Start column
                grid.BeginEdit();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var editingBox = grid.Columns[1].GetCellContent(item) as TextBox ?? throw new InvalidOperationException("Start cell did not enter edit mode.");
                editingBox.Text = "10:00:00";   // teacher has typed, not yet committed
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                // a routine background sync lands mid-edit
                messenger.Send(new DataChanged(CacheTable.Periods));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                bool preserved = editor.Periods.Count > 0 && editor.Periods[0].Start == TimeSpan.FromHours(10);
                window.Close();
                Assert.True(
                    preserved || editor.HasConflict,
                    "An in-progress cell edit was discarded by the remote-change reload without latching a conflict.");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static TimetableEditorViewModel Editor(IMessenger messenger, params Timetable[] rows) => new(new Gateway(), new Sync(), new Timetables(rows), new Week(), new Overrides(), new Windows(), messenger);
    private sealed class Timetables(params Timetable[] rows) : ITimetableRepository { public Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Timetable>>(rows); }
    private sealed class Week(params WeekScheduleEntry[] entries) : IWeekScheduleRepository { public Task<WeekSchedule> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(entries.Length == 0 ? WeekSchedule.Empty : new WeekSchedule(entries)); }
    private sealed class Overrides(params DateOverride[] rows) : IDateOverrideRepository { public Task<IReadOnlyList<DateOverride>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DateOverride>>(rows); }
    private sealed class Announcements(params Announcement[] rows) : IAnnouncementRepository { public Task<IReadOnlyList<Announcement>> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Announcement>>(rows); }
    private sealed class Profiles(params Profile[] rows) : IProfileRepository { public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Profile>>(rows); public Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(rows.FirstOrDefault(x => x.Id == id)); }
    private sealed class Classes(params AqiClock.Domain.Entities.Class[] rows) : IClassRepository { public Task<IReadOnlyList<AqiClock.Domain.Entities.Class>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AqiClock.Domain.Entities.Class>>(rows); public Task<IReadOnlySet<Guid>> GetClassIdsForPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>()); }
    private sealed class Cache : ILocalCache
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WipeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReplaceSnapshotAsync(CacheSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetMetaAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetMetaAsync(string key, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DateTimeOffset?> GetLastSyncedAtAsync(CacheTable table, CancellationToken cancellationToken = default) => Task.FromResult<DateTimeOffset?>(null);
    }
    private sealed class Settings : ISettingsService { public AppSettings Current => new(); public event EventHandler<SettingsChanged>? Changed { add { } remove { } } public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class Session(Guid? id = null) : ISessionService { public SessionState Current { get; } = new(id ?? Guid.NewGuid(), "admin@example.test", UserRole.Admin, true, false); public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SignInAsync(string email, string password, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class Sync : ISyncService { public ConnectivityState State { get; set; } = ConnectivityState.Online; public DateTimeOffset? LastSyncedAt => DateTimeOffset.UtcNow; public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SyncAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SyncTableAsync(CacheTable table, CancellationToken cancellationToken = default) => Task.CompletedTask; public void SignalTableChanged(CacheTable table) { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class EchoSync(IMessenger messenger) : ISyncService { public ConnectivityState State => ConnectivityState.Online; public DateTimeOffset? LastSyncedAt => DateTimeOffset.UtcNow; public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SyncAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SyncTableAsync(CacheTable table, CancellationToken cancellationToken = default) { messenger.Send(new DataChanged(table)); return Task.CompletedTask; } public void SignalTableChanged(CacheTable table) { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class Windows(bool confirmResult = true) : IWindowService { public bool AdminClosed { get; private set; } public string? CloseReason { get; private set; } public void ShowMainWindow() { } public void ShowSignInWindow() { } public void ShowPasswordRecoveryWindow(PasswordRecoveryRequest request) { } public void ClosePasswordRecoveryWindow() { } public void ShowSettingsWindow() { } public void ShowAdminWindow() { } public void CloseAdminWindow(string? reason = null) { AdminClosed = true; CloseReason = reason; } public bool Confirm(string message, string title) => confirmResult; public void ShowAnnouncements() { } public void HideMainWindow() { } public void ActivateMainWindow() { } public void CloseSignInWindow() { } public void ShutdownApplication() { } public void ExitApplication() { } }
    private sealed class Gateway : ISupabaseGateway
    {
        public Task CompletePasswordRecoveryAsync(string accessToken, string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Exception? ProfileFailure { get; init; }
        public Exception? WriteFailure { get; set; }
        public Exception? DeleteFailure { get; set; }
        public object? LastUpdatedRow { get; private set; }
        public object? LastInsertedRow { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int ProfileUpdateCalls { get; private set; }
        public TimetableRow? SavedTimetable { get; private set; }
        public IReadOnlyList<PeriodRow>? SavedPeriods { get; private set; }
        public int? SavedWeekday { get; private set; }
        public Guid? SavedAudienceClassId { get; private set; }
        public Guid? SavedWeekTimetableId { get; private set; }
        public int? DeletedWeekday { get; private set; }
        public Guid? DeletedAudienceClassId { get; private set; }
        public Task SaveTimetableAsync(TimetableRow timetable, IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default) { SavedTimetable = timetable; SavedPeriods = periods; return WriteFailure is null ? Task.CompletedTask : Task.FromException(WriteFailure); }
        public Task SaveWeekScheduleRowAsync(int weekday, Guid? audienceClassId, Guid? timetableId, CancellationToken cancellationToken = default) { SavedWeekday = weekday; SavedAudienceClassId = audienceClassId; SavedWeekTimetableId = timetableId; return WriteFailure is null ? Task.CompletedTask : Task.FromException(WriteFailure); }
        public Task DeleteWeekScheduleRowAsync(int weekday, Guid audienceClassId, CancellationToken cancellationToken = default) { DeletedWeekday = weekday; DeletedAudienceClassId = audienceClassId; return DeleteFailure is null ? Task.CompletedTask : Task.FromException(DeleteFailure); }
        public Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException(); public Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException(); public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid()); public Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default) => throw new NotSupportedException(); public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) { LastInsertedRow = row; return WriteFailure is null ? Task.CompletedTask : Task.FromException(WriteFailure); } public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) { UpdateCalls++; LastUpdatedRow = row; return WriteFailure is null ? Task.CompletedTask : Task.FromException(WriteFailure); } public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) { DeleteCalls++; return DeleteFailure is null ? Task.CompletedTask : Task.FromException(DeleteFailure); } public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default) { ProfileUpdateCalls++; return ProfileFailure is null ? Task.CompletedTask : Task.FromException(ProfileFailure); } public Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEntry>>([]); public Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CaptureListener(List<string> errors) : TraceListener
    {
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            T? child = FindVisualChild<T>(VisualTreeHelper.GetChild(parent, index));
            if (child is not null) return child;
        }
        return null;
    }
}
