using AqiClock.App.Converters;
using AqiClock.App.ViewModels;
using AqiClock.App.Views;
using System.Globalization;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using CommunityToolkit.Mvvm.Messaging;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AqiClock.Application.Tests;

public sealed class AdminViewModelTests
{
    [Fact]
    public async Task GeneratedTimetableUsesPreviewAndDisablesLegacyCommands()
    {
        Guid timetableId = Guid.NewGuid(); Guid blockId = Guid.NewGuid(); Guid anchorId = Guid.NewGuid();
        var timetable = new Timetable(timetableId, "Generated", false, []);
        var gateway = new Gateway
        {
            GeneratorSnapshot = new(new(timetableId, Guid.NewGuid(), "pm", new(18, 15), null, "Lesson {number}"),
                [new(blockId, timetableId, Guid.NewGuid(), 0, "lessons", null, 2, 25, null, false)],
                [new(timetableId, anchorId, Guid.NewGuid())]),
            AnchorSnapshot = new([new(anchorId, Guid.NewGuid(), "asr", "Asr", 0)],
                [new(Guid.NewGuid(), Guid.NewGuid(), anchorId, null, new(18, 40), 10, new(2020, 1, 1))], [])
        };
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable), new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());

        await vm.LoadAsync();

        Assert.True(vm.IsGenerated);
        Assert.False(vm.AddPeriodCommand.CanExecute(null));
        Assert.NotEmpty(vm.GeneratorPreview);
        await vm.SaveGeneratorCommand.ExecuteAsync(null);
        Assert.NotNull(gateway.SavedGenerator);
    }

    [Fact]
    public async Task GeneratorSaveStopsWhenServerPreviewDiffersAndRequiresSecondAcceptance()
    {
        Guid timetableId = Guid.NewGuid(); Guid blockId = Guid.NewGuid();
        var gateway = new Gateway
        {
            GeneratorSnapshot = new(new(timetableId, Guid.NewGuid(), "am", new(9, 0), null, "Lesson {number}"),
                [new(blockId, timetableId, Guid.NewGuid(), 0, "lessons", null, 1, 30, null, false)], []),
            PreviewNameSuffix = " (server)",
        };
        var vm = new TimetableEditorViewModel(gateway, new Sync(),
            new Timetables(new Timetable(timetableId, "Generated", false, [])),
            new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync();

        await vm.SaveGeneratorCommand.ExecuteAsync(null);

        Assert.Null(gateway.SavedGenerator);
        Assert.Contains("differs", vm.GeneratorMessage);
        Assert.EndsWith("(server)", Assert.Single(vm.GeneratorPreview).Name);

        await vm.SaveGeneratorCommand.ExecuteAsync(null);
        Assert.NotNull(gateway.SavedGenerator);
    }

    [Fact]
    public async Task GeneratedClassTimetablesWithDifferentClockLabelsShowClashWarning()
    {
        Guid orgId = Guid.NewGuid(), leftId = Guid.NewGuid(), rightId = Guid.NewGuid();
        Guid leftClassId = Guid.NewGuid(), rightClassId = Guid.NewGuid();
        var left = new Timetable(leftId, "Left", false,
            [new Period(Guid.NewGuid(), "Lesson 3", new(19, 0), new(19, 30), 0)]);
        var right = new Timetable(rightId, "Right", false,
            [new Period(Guid.NewGuid(), "Maghrib", new(19, 29), new(19, 39), 0, false)]);
        var gateway = new Gateway();
        gateway.GeneratedTimetableIds.UnionWith([leftId, rightId]);
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(left, right),
            new Week(
                new(Guid.NewGuid(), DayOfWeek.Monday, leftClassId, leftId),
                new(Guid.NewGuid(), DayOfWeek.Monday, rightClassId, rightId)),
            new Overrides(),
            new Classes(new(leftClassId, "Class A", 0), new(rightClassId, "Class B", 1)),
            new Windows(), new WeakReferenceMessenger());

        await vm.LoadAsync();

        Assert.Equal("Cross-class clock clash: Monday: Class A 'Lesson 3' and Class B 'Maghrib' disagree from 19:29 to 19:30.",
            vm.ClashWarningMessage);
    }

    [Fact]
    public async Task GeneratedClassTimetablesWithSameClockLabelStaySilent()
    {
        Guid leftId = Guid.NewGuid(), rightId = Guid.NewGuid();
        Guid leftClassId = Guid.NewGuid(), rightClassId = Guid.NewGuid();
        var left = new Timetable(leftId, "Left", false,
            [new Period(Guid.NewGuid(), "Lesson 3", new(19, 0), new(19, 30), 0)]);
        var right = new Timetable(rightId, "Right", false,
            [new Period(Guid.NewGuid(), "Lesson 3", new(19, 0), new(19, 30), 0)]);
        var gateway = new Gateway();
        gateway.GeneratedTimetableIds.UnionWith([leftId, rightId]);
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(left, right),
            new Week(
                new(Guid.NewGuid(), DayOfWeek.Monday, leftClassId, leftId),
                new(Guid.NewGuid(), DayOfWeek.Monday, rightClassId, rightId)),
            new Overrides(), new Windows(), new WeakReferenceMessenger());

        await vm.LoadAsync();

        Assert.Null(vm.ClashWarningMessage);
    }

    [Fact]
    public async Task DuplicateClassScheduleRowsReportAmbiguousClashResolution()
    {
        Guid classId = Guid.NewGuid(), timetableId = Guid.NewGuid();
        var timetable = new Timetable(timetableId, "Generated", false, []);
        var gateway = new Gateway();
        gateway.GeneratedTimetableIds.Add(timetableId);
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable),
            new Week(
                new(Guid.NewGuid(), DayOfWeek.Monday, classId, timetableId),
                new(Guid.NewGuid(), DayOfWeek.Monday, classId, timetableId)),
            new Overrides(), new Windows(), new WeakReferenceMessenger());

        await vm.LoadAsync();

        Assert.Equal("Cross-class clash check unavailable: the week schedule has duplicate class rows.",
            vm.ClashWarningMessage);
    }

    [Fact]
    public async Task MonthlyMaghribPasteRejectsWholeMonthBeforeGatewayCall()
    {
        Guid anchorId = Guid.NewGuid();
        var timetable = new Timetable(Guid.NewGuid(), "Day", false, []);
        var gateway = new Gateway { AnchorSnapshot = new([new(anchorId, Guid.NewGuid(), "maghrib", "Maghrib", 0)], [], []) };
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable), new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync(); vm.BulkMonth = new DateTime(2030, 2, 1); vm.MaghribBulkText = "18:00\nnot-a-time";

        await vm.ApplyMaghribBulkPasteCommand.ExecuteAsync(null);

        Assert.Contains("exactly 28", vm.BulkMessage);
        Assert.Equal(0, gateway.BulkCalls);
    }

    [Fact]
    public async Task LegacyConversionUsesServerPreviewAndPostsThatExactPayload()
    {
        Guid timetableId = Guid.NewGuid();
        var timetable = new Timetable(timetableId, "Legacy", false,
        [
            new(Guid.NewGuid(), "Class 1", new(9, 0), new(9, 30), 0, true),
            new(Guid.NewGuid(), "Class 2", new(9, 30), new(10, 0), 1, true),
            new(Guid.NewGuid(), "Break", new(10, 0), new(10, 10), 2, false),
            new(Guid.NewGuid(), "Class 3", new(10, 10), new(10, 40), 3, true),
        ]);
        var gateway = new Gateway();
        var vm = new TimetableEditorViewModel(gateway, new Sync(), new Timetables(timetable), new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync();

        await vm.PreviewConversionCommand.ExecuteAsync(null);
        Assert.True(vm.HasConversionPreview);
        Assert.NotEmpty(vm.ConversionDiff); // Stable generated ids intentionally replace legacy ids.
        IReadOnlyList<PeriodRow> serverRows = Assert.IsType<GeneratorServerPreview>(gateway.LastPreview).Periods;

        await vm.ConfirmConversionCommand.ExecuteAsync(null);

        Assert.True(vm.IsGenerated);
        Assert.Equal(serverRows, gateway.SavedGenerator?.Periods);
    }

    [Fact]
    public async Task LegacyConversionRefusesAmbiguousNonContiguousRows()
    {
        Guid timetableId = Guid.NewGuid();
        var timetable = new Timetable(timetableId, "Ambiguous", false,
        [
            new(Guid.NewGuid(), "Lesson 1", new(9, 0), new(9, 30), 0, true),
            new(Guid.NewGuid(), "Lesson 2", new(9, 35), new(10, 5), 1, true),
        ]);
        var vm = new TimetableEditorViewModel(new Gateway(), new Sync(), new Timetables(timetable), new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync();

        await vm.PreviewConversionCommand.ExecuteAsync(null);

        Assert.False(vm.HasConversionPreview);
        Assert.Contains("not contiguous", vm.ConversionMessage);
    }

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
    public async Task InsertBreakShiftsEveryLaterRowAndClosesSeams()
    {
        var timetable = Day(
            ("Lesson 1", new(9, 0), new(10, 0)),
            ("Lesson 2", new(10, 0), new(11, 0)),
            ("Lesson 3", new(11, 0), new(12, 0)));
        var vm = Editor(new WeakReferenceMessenger(), timetable);
        await vm.LoadAsync();
        vm.BreakName = "Naseehah"; vm.BreakMinutes = 20;

        vm.InsertBreakCommand.Execute(vm.Periods[0]);

        Assert.Equal(["Lesson 1", "Naseehah", "Lesson 2", "Lesson 3"], vm.Periods.Select(item => item.Name));
        Assert.False(vm.Periods[1].IsLesson);
        Assert.Equal(new TimeSpan(10, 0, 0), vm.Periods[1].Start);
        Assert.Equal(new TimeSpan(10, 20, 0), vm.Periods[1].End);
        Assert.Equal(new TimeSpan(10, 20, 0), vm.Periods[2].Start);
        Assert.Equal(new TimeSpan(12, 20, 0), vm.Periods[3].End);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task ShiftLaterAcceptsNegativeMinutesAndClosesPrecedingSeam()
    {
        var timetable = Day(
            ("Lesson 1", new(9, 0), new(10, 0)),
            ("Salah", new(10, 0), new(10, 20)),
            ("Lesson 2", new(10, 20), new(11, 20)));
        var vm = Editor(new WeakReferenceMessenger(), timetable);
        await vm.LoadAsync(); vm.ShiftMinutes = -5;

        vm.ShiftLaterCommand.Execute(vm.Periods[1]);

        Assert.Equal(new TimeSpan(9, 55, 0), vm.Periods[0].End);
        Assert.Equal(new TimeSpan(9, 55, 0), vm.Periods[1].Start);
        Assert.Equal(new TimeSpan(10, 15, 0), vm.Periods[1].End);
        Assert.Equal(new TimeSpan(10, 15, 0), vm.Periods[2].Start);
    }

    [Fact]
    public async Task ShiftAcrossMidnightIsRejectedWithoutMutation()
    {
        var timetable = Day(("Late lesson", new(23, 0), new(23, 50)));
        var vm = Editor(new WeakReferenceMessenger(), timetable);
        await vm.LoadAsync(); vm.ShiftMinutes = 10;
        TimeSpan originalStart = vm.Periods[0].Start; TimeSpan originalEnd = vm.Periods[0].End;

        vm.ShiftLaterCommand.Execute(vm.Periods[0]);

        Assert.Equal(originalStart, vm.Periods[0].Start);
        Assert.Equal(originalEnd, vm.Periods[0].End);
        Assert.False(vm.IsDirty);
        Assert.Contains("00:00–23:59", vm.ValidationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InsertBreakDisambiguatesDuplicateName()
    {
        var timetable = Day(
            ("Salah", new(9, 0), new(9, 20)),
            ("Lesson 1", new(9, 20), new(10, 0)));
        var vm = Editor(new WeakReferenceMessenger(), timetable);
        await vm.LoadAsync(); vm.BreakName = "salah"; vm.BreakMinutes = 10;

        vm.InsertBreakCommand.Execute(vm.Periods[0]);

        Assert.Equal("salah (2)", vm.Periods[1].Name);
        Assert.True(vm.Validate());
    }

    [Fact]
    public async Task InsertSaveAndReloadPreservesReflowedTimes()
    {
        var repository = new Timetables(Day(
            ("Lesson 1", new(9, 0), new(10, 0)),
            ("Lesson 2", new(10, 0), new(11, 0))));
        var gateway = new Gateway();
        gateway.OnTimetableSaved = (row, periods) => repository.Rows =
        [
            new Timetable(row.Id, row.Name, row.IsArchived, periods.Select(period =>
                new Period(period.Id, period.Name, period.StartTime, period.EndTime, period.SortOrder, period.IsLesson)).ToArray()),
        ];
        var vm = new TimetableEditorViewModel(gateway, new Sync(), repository, new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());
        await vm.LoadAsync(); vm.BreakName = "Salah"; vm.BreakMinutes = 15;

        vm.InsertBreakCommand.Execute(vm.Periods[0]);
        Guid insertedId = vm.Periods[1].Id;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), new TimeSpan(10, 15, 0)], vm.Periods.Select(item => item.Start));
        Assert.Equal(insertedId, vm.Periods[1].Id);
        Assert.Equal(new TimeSpan(11, 15, 0), vm.Periods[2].End);
        Assert.False(vm.IsDirty);
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

    [Fact]
    public async Task DiscardedPeriodRowsStopMarkingTheEditorDirty()
    {
        Period period = new(Guid.NewGuid(), "Lesson 1", new(9, 10), new(9, 40), 0);
        var timetable = new Timetable(Guid.NewGuid(), "Normal Day", false, [period]);
        var vm = Editor(new WeakReferenceMessenger(), timetable);
        await vm.LoadAsync();

        PeriodEditorItem discarded = vm.Periods[0];
        await vm.LoadAsync();                       // rebuilds Periods with fresh instances
        Assert.NotSame(discarded, vm.Periods[0]);
        Assert.False(vm.IsDirty);

        discarded.Start = new(11, 0, 0);            // a row no longer shown must not dirty the editor
        Assert.False(vm.IsDirty);

        PeriodEditorItem removed = vm.Periods[0];
        vm.RemovePeriodCommand.Execute(removed);
        vm.IsDirty = false;
        removed.Start = new(12, 0, 0);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void PartialCellEntryStillLatchesConflictOnRemoteChange()
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
                grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[1]);
                grid.BeginEdit();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Assert.True(editor.IsDirty, "Opening a cell editor must mark the editor dirty.");

                var editingBox = grid.Columns[1].GetCellContent(item) as TextBox ?? throw new InvalidOperationException("Start cell did not enter edit mode.");
                editingBox.Text = "1:";             // half-typed: cannot convert to TimeSpan, so it never reaches the view model
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Assert.Equal(new TimeSpan(9, 10, 0), editor.Periods[0].Start);

                messenger.Send(new DataChanged(CacheTable.Periods));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                bool latched = editor.HasConflict;
                window.Close();
                Assert.True(latched, "A partially typed cell value was discarded by the remote-change reload without latching a conflict.");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Theory]
    [InlineData(9, 10, "09:10")]
    [InlineData(13, 0, "13:00")]
    [InlineData(0, 5, "00:05")]
    [InlineData(23, 59, "23:59")]
    public void PeriodTimesRenderWithoutSeconds(int hours, int minutes, string expected) =>
        Assert.Equal(expected, new HourMinuteConverter().Convert(new TimeSpan(hours, minutes, 0), typeof(string), null!, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("9:10", 9, 10)]
    [InlineData("09:10", 9, 10)]
    [InlineData("13:30", 13, 30)]
    [InlineData(" 17:05 ", 17, 5)]
    public void PeriodTimesAcceptHourMinuteEntry(string entry, int hours, int minutes) =>
        Assert.Equal(new TimeSpan(hours, minutes, 0), new HourMinuteConverter().ConvertBack(entry, typeof(TimeSpan), null!, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("1")]          // TimeSpan.Parse would read this as one whole day
    [InlineData("26:00")]      // and this as twenty-six hours
    [InlineData("13:60")]
    [InlineData("13:00:00")]   // seconds are no longer part of the contract
    [InlineData("half nine")]
    [InlineData("")]
    public void PeriodTimesRejectEntriesThatAreNotAWallClockMinute(string entry) =>
        Assert.Equal(DependencyProperty.UnsetValue, new HourMinuteConverter().ConvertBack(entry, typeof(TimeSpan), null!, CultureInfo.InvariantCulture));

    private static Timetable Day(params (string Name, TimeOnly Start, TimeOnly End)[] periods) =>
        new(Guid.NewGuid(), "Day", false, periods.Select((period, index) => new Period(Guid.NewGuid(), period.Name, period.Start, period.End, index)).ToArray());
    private static TimetableEditorViewModel Editor(IMessenger messenger, params Timetable[] rows) => new(new Gateway(), new Sync(), new Timetables(rows), new Week(), new Overrides(), new Windows(), messenger);
    private sealed class Timetables(params Timetable[] rows) : ITimetableRepository { public IReadOnlyList<Timetable> Rows { get; set; } = rows; public Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(Rows); }
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
        public GeneratorAuthoringSnapshot? GeneratorSnapshot { get; init; }
        public HashSet<Guid> GeneratedTimetableIds { get; } = [];
        public AnchorConfigurationSnapshot AnchorSnapshot { get; init; } = new([], [], []);
        public (Guid TimetableId, IReadOnlyList<PeriodRow> Periods)? SavedGenerator { get; private set; }
        public GeneratorServerPreview? LastPreview { get; private set; }
        public string PreviewNameSuffix { get; init; } = string.Empty;
        public int BulkCalls { get; private set; }
        public Action<TimetableRow, IReadOnlyList<PeriodRow>>? OnTimetableSaved { get; set; }
        public int? SavedWeekday { get; private set; }
        public Guid? SavedAudienceClassId { get; private set; }
        public Guid? SavedWeekTimetableId { get; private set; }
        public int? DeletedWeekday { get; private set; }
        public Guid? DeletedAudienceClassId { get; private set; }
        public Task SaveTimetableAsync(TimetableRow timetable, IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default) { SavedTimetable = timetable; SavedPeriods = periods; if (WriteFailure is null) OnTimetableSaved?.Invoke(timetable, periods); return WriteFailure is null ? Task.CompletedTask : Task.FromException(WriteFailure); }
        public Task<GeneratorAuthoringSnapshot> GetGeneratorAuthoringAsync(Guid timetableId, CancellationToken cancellationToken = default)
        {
            if (GeneratorSnapshot?.Definition?.TimetableId == timetableId) return Task.FromResult(GeneratorSnapshot);
            if (GeneratedTimetableIds.Contains(timetableId))
                return Task.FromResult(new GeneratorAuthoringSnapshot(
                    new(timetableId, Guid.NewGuid(), "pm", new(18, 15), null, "Lesson {number}"), [], []));
            return Task.FromResult(new GeneratorAuthoringSnapshot(null, [], []));
        }
        public Task<AnchorConfigurationSnapshot> GetAnchorConfigurationAsync(CancellationToken cancellationToken = default) => Task.FromResult(AnchorSnapshot);
        public Task SaveGeneratedTimetableAsync(Guid timetableId, GeneratorDefinitionWrite definition, IReadOnlyList<GeneratorBlockWrite> blocks, IReadOnlyList<Guid> anchorIds, IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default) { SavedGenerator = (timetableId, periods); return Task.CompletedTask; }
        public Task<GeneratorServerPreview> PreviewGeneratedTimetableAsync(Guid timetableId, GeneratorDefinitionWrite definition, IReadOnlyList<GeneratorBlockWrite> blocks, IReadOnlyList<Guid> anchorIds, CancellationToken cancellationToken = default)
        {
            GeneratorResult result = AlQalamExpansionRules.Expand(timetableId,
                definition.SessionKind == "am" ? GeneratorSessionKind.Am : GeneratorSessionKind.Pm,
                definition.DayStart,
                blocks.Select(block => new GeneratorBlock(block.Id,
                    block.BlockKind == "break" ? GeneratorBlockKind.Break : GeneratorBlockKind.Lessons,
                    block.Name ?? string.Empty, block.LessonCount ?? 1,
                    block.LessonMinutes ?? block.BreakMinutes ?? 1, block.HostsNaseehah)).ToArray(),
                AnchorSnapshot.Anchors.Where(anchor => anchorIds.Contains(anchor.Id)).Select(anchor =>
                {
                    AnchorStandingTime? standing = AnchorSnapshot.StandingTimes.FirstOrDefault(row => row.AnchorId == anchor.Id);
                    return new ResolvedAnchor(anchor.Id, anchor.Key, anchor.Name,
                        standing?.StartTime ?? new TimeOnly(23, 59), standing?.DurationMinutes);
                }).ToArray(), definition.AdvisoryDayEnd, definition.NamingPattern);
            LastPreview = new(new DateOnly(2035, 1, 8), result.Periods.Select((period, index) =>
                new PeriodRow(period.Id, timetableId, period.Name + PreviewNameSuffix, period.Start, period.End, index, period.IsLesson)).ToArray());
            return Task.FromResult(LastPreview);
        }
        public Task<int> BulkUpsertAnchorDateOverridesAsync(Guid anchorId, IReadOnlyList<AnchorDateOverrideWrite> rows, CancellationToken cancellationToken = default) { BulkCalls++; return Task.FromResult(rows.Count); }
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
