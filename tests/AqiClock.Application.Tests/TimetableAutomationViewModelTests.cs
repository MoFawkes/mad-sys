using AqiClock.App.Services;
using AqiClock.App.ViewModels;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.Application.Tests;

public sealed class TimetableAutomationViewModelTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);

    [Fact]
    public async Task PreviewDateRepreviewsWithoutMarkingShapeDirty()
    {
        Timetable timetable = EmptyTimetable("Generated");
        var gateway = new FakeGateway { Today = Today };
        gateway.Shapes[timetable.Id] = DefaultShape();
        using TimetableEditorViewModel vm = Editor(gateway, new TimetableRepository(timetable));
        await vm.LoadAsync();
        int callsAfterLoad = gateway.PreviewCalls;

        vm.PreviewDate = Today.AddDays(7).ToDateTime(TimeOnly.MinValue);
        await vm.RefreshPreviewCommand.ExecuteAsync(null);

        Assert.False(vm.IsDirty);
        Assert.Equal(callsAfterLoad + 1, gateway.PreviewCalls);
        Assert.Equal(Today.AddDays(7), gateway.LastPreviewDate);
    }

    [Fact]
    public async Task SwitchingTimetableResetsPreviewDateToOrganizationToday()
    {
        Timetable first = EmptyTimetable("A timetable");
        Timetable second = EmptyTimetable("B timetable");
        var gateway = new FakeGateway { Today = Today };
        gateway.Shapes[first.Id] = DefaultShape();
        gateway.Shapes[second.Id] = DefaultShape();
        using TimetableEditorViewModel vm = Editor(gateway, new TimetableRepository(first, second));
        await vm.LoadAsync();
        vm.PreviewDate = Today.AddDays(10).ToDateTime(TimeOnly.MinValue);

        vm.Selected = second;
        await WaitUntilAsync(() => gateway.LastPreviewTimetableId == second.Id);

        Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), vm.PreviewDate);
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(367)]
    public async Task OutOfRangePreviewDateIsRejectedBeforeGateway(int offsetDays)
    {
        Timetable timetable = EmptyTimetable("Generated");
        var gateway = new FakeGateway { Today = Today };
        gateway.Shapes[timetable.Id] = DefaultShape();
        using TimetableEditorViewModel vm = Editor(gateway, new TimetableRepository(timetable));
        await vm.LoadAsync();
        int callsAfterLoad = gateway.PreviewCalls;

        vm.PreviewDate = Today.AddDays(offsetDays).ToDateTime(TimeOnly.MinValue);
        await vm.RefreshPreviewCommand.ExecuteAsync(null);

        Assert.Equal(callsAfterLoad, gateway.PreviewCalls);
        Assert.Equal($"Choose a date between today and {Today.AddDays(366):dd MMM yyyy}.", vm.AutomationMessage);
        Assert.Empty(vm.GeneratorPreview);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task AutomaticShapeValidationReportsEveryRulePrecisely()
    {
        Timetable timetable = EmptyTimetable("Generated");
        var gateway = new FakeGateway { Today = Today };
        gateway.Shapes[timetable.Id] = DefaultShape();
        using TimetableEditorViewModel vm = Editor(gateway, new TimetableRepository(timetable));
        await vm.LoadAsync();

        vm.LessonCount = 0;
        AssertValidation(vm, "Enter between 1 and 20 lessons.");

        vm.LessonCount = 6;
        vm.LessonMinutes = 4;
        AssertValidation(vm, "Enter a lesson length between 5 and 120 minutes.");

        vm.LessonMinutes = 25;
        vm.HasBreak = true;
        vm.BreakAfterLesson = 6;
        AssertValidation(vm, "The break must come after lesson 1 and before lesson 6.");

        vm.BreakAfterLesson = 3;
        vm.AutomaticBreakMinutes = 4;
        AssertValidation(vm, "Enter a break length between 5 and 120 minutes.");

        vm.HasBreak = false;
        vm.DayStart = new(23, 0, 0);
        AssertValidation(vm, "The day would run past midnight. Reduce the number of lessons or start earlier.");
    }

    [Fact]
    public async Task EnablingAutomaticModeInfersPartTimeTwoShape()
    {
        var periods = Enumerable.Range(0, 6).Select(index => new Period(
            Guid.NewGuid(), $"Lesson {index + 1}", new TimeOnly(17, 50).AddMinutes(index * 25),
            new TimeOnly(17, 50).AddMinutes((index + 1) * 25), index)).ToArray();
        var timetable = new Timetable(Guid.NewGuid(), "Part-Time 2", false, periods);
        var gateway = new FakeGateway { Today = Today };
        using TimetableEditorViewModel vm = Editor(gateway, new TimetableRepository(timetable));
        await vm.LoadAsync();

        vm.IsAutomatic = true;

        Assert.Equal(new TimeSpan(17, 50, 0), vm.DayStart);
        Assert.Equal(6, vm.LessonCount);
        Assert.Equal(25, vm.LessonMinutes);
        Assert.False(vm.HasBreak);
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public async Task RaggedTimetableLeavesAutomaticDefaultsWithoutAnError()
    {
        var timetable = new Timetable(Guid.NewGuid(), "Ragged", false,
        [
            new(Guid.NewGuid(), "Lesson 1", new(9, 0), new(9, 20), 0),
            new(Guid.NewGuid(), "Lesson 2", new(9, 20), new(9, 50), 1),
        ]);
        var gateway = new FakeGateway { Today = Today };
        using TimetableEditorViewModel vm = Editor(gateway, new TimetableRepository(timetable));
        await vm.LoadAsync();

        vm.IsAutomatic = true;

        Assert.Equal(new TimeSpan(18, 15, 0), vm.DayStart);
        Assert.Equal(6, vm.LessonCount);
        Assert.Equal(25, vm.LessonMinutes);
        Assert.False(vm.HasBreak);
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public async Task FixedPrayerTimesKeepOnlyAsrIshaAndFridayRowsWithOneEffectiveDate()
    {
        Guid orgId = Guid.NewGuid();
        Guid zuhrId = Guid.NewGuid();
        Guid asrId = Guid.NewGuid();
        Guid ishaId = Guid.NewGuid();
        DateOnly effective = Today.AddDays(2);
        var gateway = new FakeGateway
        {
            Today = Today,
            PrayerSnapshot = new(
                [new(zuhrId, orgId, "zuhr", "Zuhr", 0), new(asrId, orgId, "asr", "Asr", 1), new(ishaId, orgId, "isha", "Isha", 3)],
                [
                    new(Guid.NewGuid(), orgId, zuhrId, null, new(13, 30), 10, Today),
                    new(Guid.NewGuid(), orgId, zuhrId, 4, new(12, 58), 30, Today),
                ], []),
        };
        var vm = new PrayerTimesViewModel(gateway);
        await vm.LoadAsync();
        vm.EffectiveFrom = effective.ToDateTime(TimeOnly.MinValue);

        await vm.SaveFixedTimesCommand.ExecuteAsync(null);

        Assert.Equal(effective, gateway.SavedFixedEffectiveFrom);
        Assert.Collection(gateway.SavedFixedRows!,
            row => Assert.Equal(("asr", (int?)null), (row.AnchorKey, row.Weekday)),
            row => Assert.Equal(("isha", (int?)null), (row.AnchorKey, row.Weekday)),
            row => Assert.Equal(("zuhr", (int?)4, new TimeOnly(12, 58)), (row.AnchorKey, row.Weekday, row.StartTime)));
    }

    [Fact]
    public async Task ClearedJumuahDurationRoundTripsAsNullRatherThanZero()
    {
        Guid orgId = Guid.NewGuid();
        Guid zuhrId = Guid.NewGuid();
        Guid asrId = Guid.NewGuid();
        Guid ishaId = Guid.NewGuid();
        var gateway = new FakeGateway
        {
            Today = Today,
            PrayerSnapshot = new(
                [new(zuhrId, orgId, "zuhr", "Zuhr", 0), new(asrId, orgId, "asr", "Asr", 1), new(ishaId, orgId, "isha", "Isha", 3)],
                [
                    new(Guid.NewGuid(), orgId, asrId, null, new(18, 15), 10, Today),
                    new(Guid.NewGuid(), orgId, ishaId, null, new(20, 0), 10, Today),
                    new(Guid.NewGuid(), orgId, zuhrId, 4, new(12, 58), 30, Today),
                ], []),
        };
        var vm = new PrayerTimesViewModel(gateway);
        await vm.LoadAsync();
        vm.FixedTimes.Single(item => item.Weekday == 4).DurationMinutes = null;

        await vm.SaveFixedTimesCommand.ExecuteAsync(null);

        PrayerFixedTimeWrite savedJumuah = gateway.SavedFixedRows!.Single(item => item.Weekday == 4);
        Assert.Null(savedJumuah.DurationMinutes);
        var reloaded = new PrayerTimesViewModel(gateway);
        await reloaded.LoadAsync();
        Assert.Null(reloaded.FixedTimes.Single(item => item.Weekday == 4).DurationMinutes);
    }

    [Fact]
    public async Task ExpiringMaghribWarningSurvivesRegenerationAndUsesPrayerTimesDirection()
    {
        Timetable timetable = EmptyTimetable("Generated");
        Guid orgId = Guid.NewGuid();
        Guid maghribId = Guid.NewGuid();
        Guid zuhrId = Guid.NewGuid();
        Guid asrId = Guid.NewGuid();
        Guid ishaId = Guid.NewGuid();
        GeneratorMaintenanceRun run = new(Guid.NewGuid(), orgId, DateTimeOffset.UtcNow, 20, Today, 0, null);
        var gateway = new FakeGateway
        {
            Today = Today,
            LatestRun = run,
            RegenerationRun = run,
            PrayerSnapshot = new(
                [
                    new(maghribId, orgId, "maghrib", "Maghrib", 0),
                    new(zuhrId, orgId, "zuhr", "Zuhr", 1),
                    new(asrId, orgId, "asr", "Asr", 2),
                    new(ishaId, orgId, "isha", "Isha", 3),
                ],
                [
                    new(Guid.NewGuid(), orgId, zuhrId, 4, new(12, 58), 30, Today),
                    new(Guid.NewGuid(), orgId, asrId, null, new(18, 15), 10, Today),
                    new(Guid.NewGuid(), orgId, ishaId, null, new(20, 0), 10, Today),
                ],
                [new(Guid.NewGuid(), orgId, maghribId, Today.AddDays(10), new(18, 30), 10, false)]),
        };
        gateway.Shapes[timetable.Id] = DefaultShape();
        using TimetableEditorViewModel vm = Editor(gateway, new TimetableRepository(timetable));

        await vm.LoadAsync();
        string expected = $"Prayer times are only entered up to {Today.AddDays(10):dd MMM}. Add the next month's times on the Prayer times screen.";
        Assert.Equal(expected, vm.AutomationMessage);

        await vm.RegenerateOnAdminEntryAsync();

        Assert.Equal(expected, vm.AutomationMessage);
        Assert.Equal(2, gateway.PrayerRangeCalls);
    }

    [Fact]
    public async Task PastPrayerEffectiveDateIsRejectedBeforeGateway()
    {
        var gateway = new FakeGateway { Today = Today };
        var vm = new PrayerTimesViewModel(gateway);
        await vm.LoadAsync();
        vm.EffectiveFrom = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue);

        await vm.SaveFixedTimesCommand.ExecuteAsync(null);

        Assert.Equal(0, gateway.SaveFixedCalls);
        Assert.Equal("Choose today or a future date.", vm.FixedTimesMessage);
    }

    [Fact]
    public async Task DisablingAutomaticModeRestoresExactHandAuthoredRows()
    {
        Guid timetableId = Guid.NewGuid();
        var original = new Timetable(timetableId, "Part-Time 2", false,
            [new Period(Guid.NewGuid(), "Lesson 4", new(19, 30), new(19, 55), 0)]);
        var generated = new Timetable(timetableId, "Part-Time 2", false,
        [
            new(Guid.NewGuid(), "Lesson 4 (part 1)", new(19, 30), new(19, 40), 0),
            new(Guid.NewGuid(), "Maghrib", new(19, 40), new(19, 50), 1, false),
            new(Guid.NewGuid(), "Lesson 4 (part 2)", new(19, 50), new(20, 5), 2),
        ]);
        var repository = new TimetableRepository(generated);
        var gateway = new FakeGateway { Today = Today };
        gateway.Shapes[timetableId] = DefaultShape();
        gateway.OnDisable = _ => repository.Rows = [original];
        using TimetableEditorViewModel vm = Editor(gateway, repository);
        await vm.LoadAsync();

        vm.IsAutomatic = false;
        await vm.SaveCommand.ExecuteAsync(null);

        PeriodEditorItem restored = Assert.Single(vm.Periods);
        Assert.Equal("Lesson 4", restored.Name);
        Assert.Equal(new TimeSpan(19, 30, 0), restored.Start);
        Assert.Equal(new TimeSpan(19, 55, 0), restored.End);
        Assert.DoesNotContain(vm.Periods, row => row.Name.Contains("part", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Periods, row => row.Name.Contains("Maghrib", StringComparison.OrdinalIgnoreCase));
        Assert.False(vm.IsAutomatic);
    }

    private static void AssertValidation(TimetableEditorViewModel vm, string expected)
    {
        Assert.False(vm.Validate());
        Assert.Equal(expected, vm.ValidationMessage);
    }

    private static TimetableShape DefaultShape() => new(new(18, 15), 6, 25, null, null, true);
    private static Timetable EmptyTimetable(string name) => new(Guid.NewGuid(), name, false, []);
    private static TimetableEditorViewModel Editor(FakeGateway gateway, TimetableRepository repository) =>
        new(gateway, new Sync(), repository, new Week(), new Overrides(), new Windows(), new WeakReferenceMessenger());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class TimetableRepository(params Timetable[] rows) : ITimetableRepository
    {
        public IReadOnlyList<Timetable> Rows { get; set; } = rows;
        public Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(Rows);
    }

    private sealed class Week : IWeekScheduleRepository
    {
        public Task<WeekSchedule> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(WeekSchedule.Empty);
    }

    private sealed class Overrides : IDateOverrideRepository
    {
        public Task<IReadOnlyList<DateOverride>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DateOverride>>([]);
    }

    private sealed class Sync : ISyncService
    {
        public ConnectivityState State => ConnectivityState.Online;
        public DateTimeOffset? LastSyncedAt => DateTimeOffset.UtcNow;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncTableAsync(CacheTable table, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SignalTableChanged(CacheTable table) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Windows : IWindowService
    {
        public void ShowMainWindow() { }
        public void ShowSignInWindow() { }
        public void ShowPasswordRecoveryWindow(PasswordRecoveryRequest request) { }
        public void ClosePasswordRecoveryWindow() { }
        public void ShowSettingsWindow() { }
        public void ShowAdminWindow() { }
        public void CloseAdminWindow(string? reason = null) { }
        public bool Confirm(string message, string title) => true;
        public void ShowAnnouncements() { }
        public void HideMainWindow() { }
        public void ActivateMainWindow() { }
        public void CloseSignInWindow() { }
        public void ShutdownApplication() { }
        public void ExitApplication() { }
    }

    private sealed class FakeGateway : ISupabaseGateway
    {
        public DateOnly Today { get; init; }
        public Dictionary<Guid, TimetableShape> Shapes { get; } = [];
        public PrayerTimesSnapshot PrayerSnapshot { get; set; } = new([], [], []);
        public GeneratorMaintenanceRun? LatestRun { get; init; }
        public GeneratorMaintenanceRun? RegenerationRun { get; init; }
        public int PrayerRangeCalls { get; private set; }
        public int PreviewCalls { get; private set; }
        public Guid? LastPreviewTimetableId { get; private set; }
        public DateOnly? LastPreviewDate { get; private set; }
        public int SaveFixedCalls { get; private set; }
        public IReadOnlyList<PrayerFixedTimeWrite>? SavedFixedRows { get; private set; }
        public DateOnly? SavedFixedEffectiveFrom { get; private set; }
        public Action<Guid>? OnDisable { get; set; }

        public Task<TimetableShape?> GetTimetableShapeAsync(Guid timetableId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TimetableShape?>(Shapes.GetValueOrDefault(timetableId));
        public Task<DateOnly> GetCurrentOrganizationDateAsync(CancellationToken cancellationToken = default) => Task.FromResult(Today);
        public Task<PrayerTimesSnapshot> GetPrayerTimesAsync(DateOnly month, CancellationToken cancellationToken = default) => Task.FromResult(PrayerSnapshot);
        public Task<PrayerTimesSnapshot> GetPrayerTimesAsync(DateOnly firstMonth, DateOnly lastMonth, CancellationToken cancellationToken = default)
        {
            PrayerRangeCalls++;
            return Task.FromResult(PrayerSnapshot);
        }
        public Task<GeneratorMaintenanceRun?> GetLatestGeneratorMaintenanceRunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LatestRun);
        public Task<GeneratorMaintenanceRun> RegenerateGeneratedTimetablesAsync(CancellationToken cancellationToken = default) =>
            RegenerationRun is { } run ? Task.FromResult(run) : Task.FromException<GeneratorMaintenanceRun>(new NotSupportedException());
        public Task<GeneratorServerPreview> PreviewGeneratedTimetableAsync(Guid timetableId, TimetableShape shape, DateOnly previewDate, CancellationToken cancellationToken = default)
        {
            PreviewCalls++;
            LastPreviewTimetableId = timetableId;
            LastPreviewDate = previewDate;
            IReadOnlyList<PeriodRow> periods = [new(Guid.NewGuid(), timetableId, "Lesson 1", shape.DayStart, shape.DayStart.AddMinutes(shape.LessonMinutes), 0, true)];
            return Task.FromResult(new GeneratorServerPreview(previewDate, periods));
        }
        public Task<IReadOnlyList<PeriodRow>> DisableGeneratedTimetableAsync(Guid timetableId, CancellationToken cancellationToken = default)
        {
            Shapes.Remove(timetableId);
            OnDisable?.Invoke(timetableId);
            return Task.FromResult<IReadOnlyList<PeriodRow>>([]);
        }
        public Task<int> SavePrayerFixedTimesAsync(IReadOnlyList<PrayerFixedTimeWrite> rows, DateOnly effectiveFrom, CancellationToken cancellationToken = default)
        {
            SaveFixedCalls++;
            SavedFixedRows = rows;
            SavedFixedEffectiveFrom = effectiveFrom;
            Dictionary<string, OrganizationAnchor> anchors = PrayerSnapshot.Anchors.ToDictionary(item => item.Key);
            AnchorStandingTime[] saved = rows.Select(row => new AnchorStandingTime(
                Guid.NewGuid(), anchors[row.AnchorKey].OrgId, anchors[row.AnchorKey].Id, row.Weekday,
                row.StartTime, row.DurationMinutes, effectiveFrom)).ToArray();
            PrayerSnapshot = PrayerSnapshot with { StandingTimes = saved };
            return Task.FromResult(rows.Count);
        }

        public Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompletePasswordRecoveryAsync(string accessToken, string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());
        public Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTimetableAsync(TimetableRow timetable, IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveWeekScheduleRowAsync(int weekday, Guid? audienceClassId, Guid? timetableId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteWeekScheduleRowAsync(int weekday, Guid audienceClassId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
        public Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
