using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.App.Services;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.App.ViewModels;

public partial class PeriodEditorItem : ObservableObject
{
    public Guid Id { get; init; }
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private TimeSpan _start;
    [ObservableProperty] private TimeSpan _end;
    [ObservableProperty] private bool _isLesson = true;
    public int SortOrder { get; set; }
}

public partial class TimetableEditorViewModel : ObservableObject, IRecipient<DataChanged>, IRecipient<PrayerTimesChanged>, IDisposable
{
    private static readonly TimeSpan AuthoringReadTimeout = TimeSpan.FromSeconds(5);
    private readonly ISupabaseGateway _gateway;
    private readonly ISyncService _sync;
    private readonly ITimetableRepository _repository;
    private readonly IWeekScheduleRepository _week;
    private readonly IDateOverrideRepository _overrides;
    private readonly IWindowService _windows;
    private readonly IClassRepository? _classes;
    private bool _loading;
    private bool _loadedAutomatic;
    private int _ownWriteDepth;
    private CancellationTokenSource? _previewDebounce;
    private DateOnly _organizationToday = DateOnly.FromDateTime(DateTime.Today);
    private string? _configurationWarning;
    private Guid? _newTimetableId;

    [ObservableProperty] private Timetable? _selected;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isArchived;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _hasConflict;
    [ObservableProperty] private string? _validationMessage;
    [ObservableProperty] private string? _warningMessage;
    [ObservableProperty] private PeriodEditorItem? _selectedPeriod;
    [ObservableProperty] private string _breakName = "Break";
    [ObservableProperty] private int _breakMinutes = 20;
    [ObservableProperty] private int _shiftMinutes;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGenerated))]
    [NotifyPropertyChangedFor(nameof(CanEditLegacyPeriods))]
    [NotifyCanExecuteChangedFor(nameof(AddPeriodCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemovePeriodCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertBreakCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShiftLaterCommand))]
    private bool _isAutomatic;
    [ObservableProperty] private TimeSpan _dayStart = new(18, 15, 0);
    [ObservableProperty] private int _lessonCount = 6;
    [ObservableProperty] private int _lessonMinutes = 25;
    [ObservableProperty] private bool _hasBreak;
    [ObservableProperty] private int _breakAfterLesson = 3;
    [ObservableProperty] private int _automaticBreakMinutes = 25;
    [ObservableProperty] private bool _adjustsForPrayer = true;
    [ObservableProperty] private DateTime _previewDate = DateTime.Today;
    [ObservableProperty] private string? _automationMessage;
    [ObservableProperty] private string? _clashWarningMessage;
    [ObservableProperty] private DateTimeOffset? _lastGeneratorRunAt;
    [ObservableProperty] private bool _isPreviewBusy;

    public bool IsGenerated => IsAutomatic;
    public bool CanEditLegacyPeriods => !IsAutomatic;
    public bool IsAutomationReadUnavailable { get; private set; }
    public bool IsMaintenanceOverdue => LastGeneratorRunAt is null || DateTimeOffset.UtcNow - LastGeneratorRunAt > TimeSpan.FromHours(48);
    public string? StatusMessage => AutomationMessage ?? ClashWarningMessage;
    public ObservableCollection<Timetable> Items { get; } = [];
    public ObservableCollection<PeriodEditorItem> Periods { get; } = [];
    public ObservableCollection<PeriodEditorItem> GeneratorPreview { get; } = [];

    public TimetableEditorViewModel(ISupabaseGateway gateway, ISyncService sync, ITimetableRepository repository,
        IWeekScheduleRepository week, IDateOverrideRepository overrides, IWindowService windows, IMessenger messenger)
    {
        _gateway = gateway; _sync = sync; _repository = repository; _week = week;
        _overrides = overrides; _windows = windows;
        Periods.CollectionChanged += OnPeriodsChanged;
        messenger.Register<DataChanged>(this);
        messenger.Register<PrayerTimesChanged>(this);
    }

    public TimetableEditorViewModel(ISupabaseGateway gateway, ISyncService sync, ITimetableRepository repository,
        IWeekScheduleRepository week, IDateOverrideRepository overrides, IClassRepository classes,
        IWindowService windows, IMessenger messenger)
        : this(gateway, sync, repository, week, overrides, windows, messenger) => _classes = classes;

    public async Task LoadAsync(CancellationToken token = default)
    {
        Guid? selectedId = Selected?.Id;
        IReadOnlyList<Timetable> rows = await _repository.GetAllAsync(token);
        _loading = true;
        try
        {
            Items.Clear();
            foreach (Timetable row in rows.OrderBy(x => x.Name)) Items.Add(row);
            Timetable? target = selectedId is { } id ? Items.FirstOrDefault(x => x.Id == id) : Items.FirstOrDefault();
            Selected = target;
            if (target is not null) Select(target);
        }
        finally { _loading = false; }
        if (Selected is not null && _sync.State == ConnectivityState.Online)
            await LoadAutomationAsync(Selected, token);
    }

    partial void OnSelectedChanged(Timetable? value)
    {
        if (_loading || value is null) return;
        Select(value);
        _ = LoadAutomationAsync(value);
    }

    private void Select(Timetable value)
    {
        _loading = true;
        try
        {
            Name = value.Name; IsArchived = value.IsArchived;
            DetachPeriodHandlers(); Periods.Clear();
            foreach (Period period in value.Periods.OrderBy(x => x.SortOrder))
                Periods.Add(new() { Id = period.Id, Name = period.Name, Start = period.StartTime.ToTimeSpan(), End = period.EndTime.ToTimeSpan(), IsLesson = period.IsLesson, SortOrder = period.SortOrder });
            IsDirty = false; HasConflict = false; ValidationMessage = null; WarningMessage = null;
        }
        finally { _loading = false; }
    }

    private void DetachPeriodHandlers() { foreach (PeriodEditorItem item in Periods) item.PropertyChanged -= OnPeriodChanged; }
    partial void OnNameChanged(string value) { if (!_loading) IsDirty = true; }
    partial void OnIsArchivedChanged(bool value) { if (!_loading) IsDirty = true; }
    private void OnPeriodsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null) foreach (PeriodEditorItem item in args.OldItems) item.PropertyChanged -= OnPeriodChanged;
        if (args.NewItems is not null) foreach (PeriodEditorItem item in args.NewItems) item.PropertyChanged += OnPeriodChanged;
        if (!_loading) IsDirty = true;
    }
    private void OnPeriodChanged(object? sender, PropertyChangedEventArgs args) { if (!_loading) IsDirty = true; }

    partial void OnIsAutomaticChanged(bool value)
    {
        if (_loading) return;
        IsDirty = true;
        if (value)
        {
            PrefillFromPeriods();
            SchedulePreview();
        }
        else
        {
            _previewDebounce?.Cancel();
            GeneratorPreview.Clear();
            AutomationMessage = _loadedAutomatic
                ? "Save to restore the hand-authored timetable that existed before automatic adjustment was enabled."
                : null;
        }
    }

    partial void OnDayStartChanged(TimeSpan value) => AutomationFieldChanged();
    partial void OnLessonCountChanged(int value) => AutomationFieldChanged();
    partial void OnLessonMinutesChanged(int value) => AutomationFieldChanged();
    partial void OnHasBreakChanged(bool value) => AutomationFieldChanged();
    partial void OnBreakAfterLessonChanged(int value) => AutomationFieldChanged();
    partial void OnAutomaticBreakMinutesChanged(int value) => AutomationFieldChanged();
    partial void OnAdjustsForPrayerChanged(bool value) => AutomationFieldChanged();
    partial void OnPreviewDateChanged(DateTime value) => AutomationFieldChanged(markDirty: false);
    partial void OnAutomationMessageChanged(string? value) => OnPropertyChanged(nameof(StatusMessage));
    partial void OnClashWarningMessageChanged(string? value) => OnPropertyChanged(nameof(StatusMessage));

    private void AutomationFieldChanged(bool markDirty = true)
    {
        if (_loading || !IsAutomatic) return;
        if (markDirty) IsDirty = true;
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        _previewDebounce?.Cancel();
        _previewDebounce?.Dispose();
        _previewDebounce = new CancellationTokenSource();
        _ = DebouncedPreviewAsync(_previewDebounce.Token);
    }

    private async Task DebouncedPreviewAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), token);
            await RefreshPreviewAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    [RelayCommand]
    private async Task RefreshPreviewAsync(CancellationToken token)
    {
        if (Selected is null || !TryBuildShape(out TimetableShape? shape)) return;
        DateOnly previewDate = DateOnly.FromDateTime(PreviewDate);
        DateOnly latestPreviewDate = _organizationToday.AddDays(366);
        if (previewDate < _organizationToday || previewDate > latestPreviewDate)
        {
            AutomationMessage = $"Choose a date between today and {latestPreviewDate:dd MMM yyyy}.";
            GeneratorPreview.Clear();
            return;
        }
        IsPreviewBusy = true;
        try
        {
            GeneratorServerPreview preview = await _gateway.PreviewGeneratedTimetableAsync(
                Selected.Id, shape!, previewDate, token);
            AdoptPreview(preview.Periods);
            AutomationMessage = preview.Periods.Count == 0
                ? "This timetable produces no rows for the selected date."
                : _configurationWarning;
        }
        catch (NotSupportedException) { AutomationMessage = "Automatic lesson times require the v0.15 server update."; }
        catch (ServerWriteException ex) { AutomationMessage = ex.Message; GeneratorPreview.Clear(); }
        finally { IsPreviewBusy = false; }
    }

    private void AdoptPreview(IEnumerable<PeriodRow> periods)
    {
        GeneratorPreview.Clear();
        foreach (PeriodRow period in periods.OrderBy(x => x.SortOrder))
            GeneratorPreview.Add(new() { Id = period.Id, Name = period.Name, Start = period.StartTime.ToTimeSpan(), End = period.EndTime.ToTimeSpan(), IsLesson = period.IsLesson, SortOrder = period.SortOrder });
    }

    private bool TryBuildShape(out TimetableShape? shape)
    {
        shape = null; ValidationMessage = null;
        if (LessonCount is < 1 or > 20) { ValidationMessage = "Enter between 1 and 20 lessons."; return false; }
        if (LessonMinutes is < 5 or > 120) { ValidationMessage = "Enter a lesson length between 5 and 120 minutes."; return false; }
        if (HasBreak && (BreakAfterLesson < 1 || BreakAfterLesson >= LessonCount))
        { ValidationMessage = $"The break must come after lesson 1 and before lesson {LessonCount}."; return false; }
        if (HasBreak && AutomaticBreakMinutes is < 5 or > 120)
        { ValidationMessage = "Enter a break length between 5 and 120 minutes."; return false; }
        int totalMinutes = checked(LessonCount * LessonMinutes + (HasBreak ? AutomaticBreakMinutes : 0));
        if (DayStart + TimeSpan.FromMinutes(totalMinutes) > new TimeSpan(23, 59, 0))
        { ValidationMessage = "The day would run past midnight. Reduce the number of lessons or start earlier."; return false; }
        shape = new(TimeOnly.FromTimeSpan(DayStart), LessonCount, LessonMinutes,
            HasBreak ? BreakAfterLesson : null, HasBreak ? AutomaticBreakMinutes : null, AdjustsForPrayer);
        return true;
    }

    private void PrefillFromPeriods()
    {
        PeriodEditorItem[] ordered = Periods.OrderBy(x => x.SortOrder).ToArray();
        PeriodEditorItem[] lessons = ordered.Where(x => x.IsLesson).ToArray();
        PeriodEditorItem[] breaks = ordered.Where(x => !x.IsLesson).ToArray();
        if (lessons.Length is < 1 or > 20) return;
        TimeSpan lessonLength = lessons[0].End - lessons[0].Start;
        if (lessonLength.TotalMinutes is < 5 or > 120 || lessons.Any(x => x.End - x.Start != lessonLength)) return;
        if (breaks.Length > 1) return;
        _loading = true;
        try
        {
            DayStart = ordered[0].Start;
            LessonCount = lessons.Length;
            LessonMinutes = (int)lessonLength.TotalMinutes;
            HasBreak = breaks.Length == 1;
            if (breaks.Length == 1)
            {
                PeriodEditorItem item = breaks[0];
                int index = Array.IndexOf(ordered, item);
                int after = ordered.Take(index).Count(x => x.IsLesson);
                int minutes = (int)(item.End - item.Start).TotalMinutes;
                if (after is >= 1 && after < lessons.Length && minutes is >= 5 and <= 120)
                { BreakAfterLesson = after; AutomaticBreakMinutes = minutes; }
                else HasBreak = false;
            }
            AdjustsForPrayer = true;
        }
        finally { _loading = false; }
    }

    private async Task LoadAutomationAsync(Timetable timetable, CancellationToken token = default)
    {
        using CancellationTokenSource authoringTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        authoringTimeout.CancelAfter(AuthoringReadTimeout);
        CancellationToken authoringToken = authoringTimeout.Token;
        try
        {
            TimetableShape? shape = await _gateway.GetTimetableShapeAsync(timetable.Id, authoringToken);
            try { _organizationToday = await _gateway.GetCurrentOrganizationDateAsync(authoringToken); }
            catch (NotSupportedException) { _organizationToday = DateOnly.FromDateTime(DateTime.Today); }
            GeneratorMaintenanceRun? run = await _gateway.GetLatestGeneratorMaintenanceRunAsync(authoringToken);
            _loading = true;
            try
            {
                PreviewDate = _organizationToday.ToDateTime(TimeOnly.MinValue);
                IsAutomatic = shape is not null;
                _loadedAutomatic = shape is not null;
                if (shape is not null)
                {
                    DayStart = shape.DayStart.ToTimeSpan(); LessonCount = shape.LessonCount; LessonMinutes = shape.LessonMinutes;
                    HasBreak = shape.BreakAfterLesson is not null; BreakAfterLesson = shape.BreakAfterLesson ?? Math.Max(1, shape.LessonCount / 2);
                    AutomaticBreakMinutes = shape.BreakMinutes ?? 25; AdjustsForPrayer = shape.AdjustsForPrayer;
                }
                LastGeneratorRunAt = run?.StartedAt;
                OnPropertyChanged(nameof(IsMaintenanceOverdue));
                IsDirty = false;
            }
            finally { _loading = false; }
            if (shape is not null)
            {
                await RefreshAutomationWarningAsync(shape.AdjustsForPrayer, run, authoringToken);
                await RefreshPreviewAsync(authoringToken);
            }
            await RefreshClashWarningsAsync(authoringToken);
            IsAutomationReadUnavailable = false;
        }
        catch (NotSupportedException)
        {
            _loading = true; IsAutomatic = false; _loadedAutomatic = false; _loading = false;
        }
        catch (HttpRequestException)
        {
            DegradeAutomationForOfflineRead();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            DegradeAutomationForOfflineRead();
        }
        catch (ServerWriteException ex) { AutomationMessage = ex.Message; }
    }

    private void DegradeAutomationForOfflineRead()
    {
        IsAutomationReadUnavailable = true;
        _loading = true;
        try { IsAutomatic = false; _loadedAutomatic = false; }
        finally { _loading = false; }
        AutomationMessage = "Automatic lesson-time editing is unavailable while offline.";
    }

    [RelayCommand]
    private void NewTimetable()
    {
        var draft = new Timetable(Guid.NewGuid(), "New timetable", false, []);
        _loading = true;
        try
        {
            Items.Add(draft); Selected = draft; _newTimetableId = draft.Id;
            Name = draft.Name; IsArchived = false;
            DetachPeriodHandlers(); Periods.Clear(); GeneratorPreview.Clear();
            DayStart = new TimeSpan(9, 0, 0); LessonCount = 6; LessonMinutes = 25;
            HasBreak = false; BreakAfterLesson = 3; AutomaticBreakMinutes = 25;
            AdjustsForPrayer = true; IsAutomatic = true; _loadedAutomatic = false;
        }
        finally { _loading = false; }
        IsDirty = true;
        SchedulePreview();
    }
    [RelayCommand(CanExecute = nameof(CanEditLegacyPeriods))] private void AddPeriod() { Periods.Add(new() { Id = Guid.NewGuid(), Name = "New period", Start = new(9, 0, 0), End = new(10, 0, 0), SortOrder = Periods.Count }); IsDirty = true; }
    [RelayCommand(CanExecute = nameof(CanEditLegacyPeriods))] private void RemovePeriod(PeriodEditorItem item) { Periods.Remove(item); IsDirty = true; }
    [RelayCommand(CanExecute = nameof(CanEditLegacyPeriods))] private void MoveUp(PeriodEditorItem item) { int index = Periods.IndexOf(item); if (index > 0) { Periods.Move(index, index - 1); IsDirty = true; } }
    [RelayCommand(CanExecute = nameof(CanEditLegacyPeriods))] private void MoveDown(PeriodEditorItem item) { int index = Periods.IndexOf(item); if (index >= 0 && index < Periods.Count - 1) { Periods.Move(index, index + 1); IsDirty = true; } }

    [RelayCommand(CanExecute = nameof(CanEditLegacyPeriods))]
    private void InsertBreak(PeriodEditorItem after)
    {
        ValidationMessage = null;
        int afterIndex = Periods.IndexOf(after);
        if (afterIndex < 0) { ValidationMessage = "Select the period after which to insert the break."; return; }
        if (BreakMinutes <= 0) { ValidationMessage = "Break length must be greater than zero minutes."; return; }
        if (string.IsNullOrWhiteSpace(BreakName)) { ValidationMessage = "Break name is required."; return; }
        if (!TryDelta(BreakMinutes, out TimeSpan delta)) return;
        int shiftIndex = afterIndex + 1;
        if (!TryPlanShift(shiftIndex, delta, validateSeam: false, out var shifted)) return;
        TimeSpan start = after.End;
        TimeSpan end = shifted.Length == 0 ? start + delta : shifted[0].Start;
        if (!IsMinuteWithinDay(start) || !IsMinuteWithinDay(end) || end <= start)
        { ValidationMessage = "That break would cross midnight or leave an invalid period boundary."; return; }
        var inserted = new PeriodEditorItem { Id = Guid.NewGuid(), Name = UniquePeriodName(BreakName.Trim()), Start = start, End = end, IsLesson = false };
        _loading = true;
        try { Periods.Insert(shiftIndex, inserted); ApplyShift(shifted); SelectedPeriod = inserted; }
        finally { _loading = false; }
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(CanEditLegacyPeriods))]
    private void ShiftLater(PeriodEditorItem? from)
    {
        ValidationMessage = null;
        int index = from is null ? -1 : Periods.IndexOf(from);
        if (index < 0) { ValidationMessage = "Select the period from which to shift later rows."; return; }
        if (ShiftMinutes == 0) { ValidationMessage = "Shift must be a non-zero number of minutes."; return; }
        if (!TryDelta(ShiftMinutes, out TimeSpan delta) || !TryPlanShift(index, delta, validateSeam: true, out var shifted)) return;
        _loading = true;
        try { ApplyShift(shifted); if (index > 0) Periods[index - 1].End = shifted[0].Start; }
        finally { _loading = false; }
        IsDirty = true;
    }

    private bool TryDelta(int minutes, out TimeSpan delta)
    {
        try { delta = TimeSpan.FromMinutes(minutes); return true; }
        catch (OverflowException) { delta = default; ValidationMessage = "The requested number of minutes is too large."; return false; }
    }

    private bool TryPlanShift(int index, TimeSpan delta, bool validateSeam, out (PeriodEditorItem Item, TimeSpan Start, TimeSpan End)[] shifted)
    {
        shifted = Periods.Skip(index).Select(item => (item, item.Start + delta, item.End + delta)).ToArray();
        if (shifted.Any(item => !IsMinuteWithinDay(item.Item2) || !IsMinuteWithinDay(item.Item3)))
        { ValidationMessage = "That shift would move a period outside 00:00–23:59."; return false; }
        if (validateSeam && index > 0 && shifted.Length > 0 && shifted[0].Item2 <= Periods[index - 1].Start)
        { ValidationMessage = "That shift would leave the preceding period with an invalid end time."; return false; }
        return true;
    }

    private static bool IsMinuteWithinDay(TimeSpan value) => SchedulingValueRules.IsMinuteWithinDay(value);
    private static void ApplyShift(IEnumerable<(PeriodEditorItem Item, TimeSpan Start, TimeSpan End)> shifted)
    { foreach (var item in shifted) { item.Item.Start = item.Start; item.Item.End = item.End; } }
    private string UniquePeriodName(string requested) => SchedulingValueRules.UniquePeriodName(requested, Periods.Select(item => item.Name));

    [RelayCommand] private void MarkDirty() => IsDirty = true;
    [RelayCommand] private void Cancel() { if (Selected is not null) { Select(Selected); _ = LoadAutomationAsync(Selected); } }
    [RelayCommand] private async Task ReloadAsync(CancellationToken token) { HasConflict = false; IsDirty = false; await LoadAsync(token); }
    [RelayCommand] private void Overwrite() => HasConflict = false;

    [RelayCommand]
    private async Task SaveAsync(CancellationToken token)
    {
        if (Selected is null) { ValidationMessage = "Select or create a timetable before saving."; return; }
        if (IsAutomatic) { await SaveAutomaticAsync(token); return; }
        if (_loadedAutomatic) { await DisableAutomaticAsync(token); return; }
        if (!ValidateManual()) return;
        _ownWriteDepth++;
        try
        {
            Guid org = await _gateway.GetCurrentOrganizationIdAsync(token);
            var row = new TimetableRow(Selected.Id, org, Name.Trim(), IsArchived);
            var periods = Periods.Select((period, index) => new PeriodRow(period.Id, Selected.Id, period.Name.Trim(),
                TimeOnly.FromTimeSpan(period.Start), TimeOnly.FromTimeSpan(period.End), index, period.IsLesson)).ToArray();
            await _gateway.SaveTimetableAsync(row, periods, token);
            _newTimetableId = null;
            await RefreshAfterWriteAsync(row.Id, token);
        }
        catch (DuplicateRowException) { ValidationMessage = "A timetable or period name is already used."; }
        catch (ServerDeniedException) { ValidationMessage = "Your role changed."; _windows.CloseAdminWindow(); }
        catch (ServerWriteException ex) { ValidationMessage = ex.Message; }
        finally { _ownWriteDepth--; }
    }

    private async Task SaveAutomaticAsync(CancellationToken token)
    {
        if (Selected is null || !ValidateName() || !TryBuildShape(out TimetableShape? shape)) return;
        bool alreadyExists = _newTimetableId != Selected.Id && Items.Any(item => item.Id == Selected.Id);
        if (alreadyExists && !_loadedAutomatic && !_windows.Confirm($"Replace the periods in '{Selected.Name}' with the automatically adjusted times shown?", "Enable automatic lesson times")) return;
        _ownWriteDepth++;
        try
        {
            if (!alreadyExists)
            {
                Guid org = await _gateway.GetCurrentOrganizationIdAsync(token);
                await _gateway.SaveTimetableAsync(new(Selected.Id, org, Name.Trim(), IsArchived), [], token);
            }
            else if (Name.Trim() != Selected.Name || IsArchived != Selected.IsArchived)
                await _gateway.UpdateAsync(CacheTable.Timetables, Selected.Id, new { name = Name.Trim(), is_archived = IsArchived }, token);
            IReadOnlyList<PeriodRow> saved = await _gateway.SaveGeneratedTimetableAsync(Selected.Id, shape!, token);
            _newTimetableId = null;
            AdoptPreview(saved);
            await RefreshAfterWriteAsync(Selected.Id, token);
            AutomationMessage = "Automatic lesson times saved.";
        }
        catch (ServerDeniedException) { ValidationMessage = "Your role changed."; _windows.CloseAdminWindow(); }
        catch (ServerWriteException ex) { ValidationMessage = ex.Message; }
        finally { _ownWriteDepth--; }
    }

    private async Task DisableAutomaticAsync(CancellationToken token)
    {
        if (Selected is null || !ValidateName()) return;
        if (!_windows.Confirm($"Turn off automatic adjustment for '{Selected.Name}' and restore its original hand-authored periods?", "Turn off automatic lesson times")) return;
        _ownWriteDepth++;
        try
        {
            await _gateway.DisableGeneratedTimetableAsync(Selected.Id, token);
            if (Name.Trim() != Selected.Name || IsArchived != Selected.IsArchived)
                await _gateway.UpdateAsync(CacheTable.Timetables, Selected.Id, new { name = Name.Trim(), is_archived = IsArchived }, token);
            await RefreshAfterWriteAsync(Selected.Id, token);
            AutomationMessage = "The original hand-authored timetable was restored.";
        }
        catch (ServerDeniedException) { ValidationMessage = "Your role changed."; _windows.CloseAdminWindow(); }
        catch (ServerWriteException ex) { ValidationMessage = ex.Message; }
        finally { _ownWriteDepth--; }
    }

    private async Task RefreshAfterWriteAsync(Guid id, CancellationToken token)
    {
        await _sync.SyncTableAsync(CacheTable.Timetables, token);
        await _sync.SyncTableAsync(CacheTable.Periods, token);
        await LoadAsync(token);
        Timetable? saved = Items.FirstOrDefault(x => x.Id == id);
        Selected = saved;
        if (saved is not null) Select(saved);
        IsDirty = false; HasConflict = false;
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken token)
    {
        if (Selected is null) return;
        List<string> used = [];
        WeekSchedule week = await _week.GetAsync(token);
        Dictionary<Guid, string> classNames = _classes is null ? [] : (await _classes.GetAllAsync(token)).ToDictionary(item => item.Id, item => item.Name);
        foreach (WeekScheduleEntry entry in week.AllEntries.Where(entry => entry.TimetableId == Selected.Id))
        {
            string qualifier = entry.AudienceClassId is { } classId && classNames.TryGetValue(classId, out string? className)
                ? $" ({className})" : entry.AudienceClassId is not null ? " (class-specific)" : string.Empty;
            used.Add($"{entry.Weekday}{qualifier}");
        }
        foreach (DateOverride item in await _overrides.GetAllAsync(token)) if (item.TimetableId == Selected.Id) used.Add(item.Date.ToString("d MMM", CultureInfo.CurrentCulture));
        if (used.Count > 0) { ValidationMessage = $"Used by: {string.Join(", ", used)} — reassign first"; return; }
        if (!_windows.Confirm($"Delete '{Selected.Name}' and all of its periods? This cannot be undone.", "Delete timetable")) return;
        try { await _gateway.DeleteAsync(CacheTable.Timetables, Selected.Id, token); await _sync.SyncTableAsync(CacheTable.Timetables, token); await _sync.SyncTableAsync(CacheTable.Periods, token); Selected = null; await LoadAsync(token); }
        catch (ReferencedRowException) { ValidationMessage = "This timetable became referenced remotely — reassign it first."; }
        catch (ServerDeniedException) { ValidationMessage = "Your role changed."; _windows.CloseAdminWindow(); }
    }

    [RelayCommand] private async Task DuplicateAsync(CancellationToken token) { if (Selected is null) return; Timetable source = Selected; NewTimetable(); Name = source.Name + " copy"; Periods.Clear(); foreach (Period p in source.Periods.OrderBy(x => x.SortOrder)) Periods.Add(new() { Id = Guid.NewGuid(), Name = p.Name, Start = p.StartTime.ToTimeSpan(), End = p.EndTime.ToTimeSpan(), IsLesson = p.IsLesson, SortOrder = p.SortOrder }); await SaveAsync(token); }
    [RelayCommand] private async Task ToggleArchiveAsync(CancellationToken token) { IsArchived = !IsArchived; await SaveAsync(token); }

    public bool Validate() => IsAutomatic ? TryBuildShape(out _) && ValidateName() : ValidateManual();
    private bool ValidateName()
    {
        ValidationMessage = null;
        if (string.IsNullOrWhiteSpace(Name)) { ValidationMessage = "Timetable name is required."; return false; }
        if (Items.Any(x => x.Id != Selected?.Id && string.Equals(x.Name, Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        { ValidationMessage = "A timetable with this name already exists."; return false; }
        return true;
    }
    private bool ValidateManual()
    {
        ValidationMessage = null; WarningMessage = null;
        if (!ValidateName()) return false;
        if (Periods.Any(x => x.End <= x.Start)) { ValidationMessage = "Every period must end after it starts."; return false; }
        if (Periods.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) { ValidationMessage = "Period names must be unique within a timetable."; return false; }
        PeriodEditorItem[] ordered = Periods.OrderBy(x => x.Start).ToArray();
        if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.End > pair.Second.Start)) WarningMessage = "Some periods overlap. Saving is allowed.";
        return true;
    }

    private async Task RefreshClashWarningsAsync(CancellationToken token)
    {
        try
        {
            IReadOnlyList<Timetable> timetables = await _repository.GetAllAsync(token);
            Dictionary<Guid, Timetable> byId = timetables.ToDictionary(item => item.Id);
            WeekSchedule schedule = await _week.GetAsync(token);
            WeekScheduleEntry[] classRows = schedule.AllEntries.Where(entry => entry.AudienceClassId is not null && entry.TimetableId is not null).ToArray();
            if (classRows.GroupBy(entry => (entry.Weekday, entry.AudienceClassId)).Any(group => group.Count() > 1))
            { ClashWarningMessage = "Cross-class clash check unavailable: the week schedule has duplicate class rows."; return; }
            Guid[] referenced = classRows.Select(entry => entry.TimetableId!.Value).Distinct().ToArray();
            var generated = new HashSet<Guid>();
            foreach (Guid timetableId in referenced)
                if (await _gateway.GetTimetableShapeAsync(timetableId, token) is not null) generated.Add(timetableId);
            Dictionary<Guid, string> classNames = _classes is null ? [] : (await _classes.GetAllAsync(token)).ToDictionary(item => item.Id, item => item.Name);
            var warnings = new List<string>();
            foreach (IGrouping<DayOfWeek, WeekScheduleEntry> day in classRows.GroupBy(entry => entry.Weekday))
            {
                WeekScheduleEntry[] candidates = day.Where(entry => generated.Contains(entry.TimetableId!.Value)).ToArray();
                for (int leftIndex = 0; leftIndex < candidates.Length; leftIndex++)
                for (int rightIndex = leftIndex + 1; rightIndex < candidates.Length; rightIndex++)
                {
                    WeekScheduleEntry left = candidates[leftIndex], right = candidates[rightIndex];
                    if (left.AudienceClassId == right.AudienceClassId || left.TimetableId == right.TimetableId) continue;
                    if (!byId.TryGetValue(left.TimetableId!.Value, out Timetable? leftTimetable) || !byId.TryGetValue(right.TimetableId!.Value, out Timetable? rightTimetable))
                    { ClashWarningMessage = "Cross-class clash check unavailable: a scheduled timetable is missing locally."; return; }
                    IReadOnlyList<GeneratedPeriodClash> clashes = GeneratedTimetableClashDetector.Find(leftTimetable.Periods, rightTimetable.Periods);
                    if (clashes.Count == 0) continue;
                    GeneratedPeriodClash clash = clashes[0];
                    string leftClass = classNames.GetValueOrDefault(left.AudienceClassId!.Value, left.AudienceClassId.Value.ToString("D"));
                    string rightClass = classNames.GetValueOrDefault(right.AudienceClassId!.Value, right.AudienceClassId.Value.ToString("D"));
                    warnings.Add($"{day.Key}: {leftClass} '{clash.Left.Name}' and {rightClass} '{clash.Right.Name}' disagree from {clash.Start:HH:mm} to {clash.End:HH:mm}.");
                }
            }
            ClashWarningMessage = warnings.Count == 0 ? null : "Cross-class clock clash: " + string.Join(" ", warnings);
        }
        catch (NotSupportedException) { ClashWarningMessage = null; }
    }

    private async Task RefreshAutomationWarningAsync(
        bool adjustsForPrayer, GeneratorMaintenanceRun? run, CancellationToken token)
    {
        _configurationWarning = await GetPrayerConfigurationWarningAsync(adjustsForPrayer, run, token);
        AutomationMessage = _configurationWarning;
    }

    private async Task<string?> GetPrayerConfigurationWarningAsync(
        bool adjustsForPrayer, GeneratorMaintenanceRun? run, CancellationToken token)
    {
        if (!adjustsForPrayer) return run?.Error;
        DateOnly nextMonth = _organizationToday.AddMonths(1);
        PrayerTimesSnapshot prayers = await _gateway.GetPrayerTimesAsync(_organizationToday, nextMonth, token);
        OrganizationAnchor? maghrib = prayers.Anchors.FirstOrDefault(item => item.Key == "maghrib");
        DateOnly? lastMaghrib = maghrib is null ? null : prayers.DateOverrides
            .Where(item => item.AnchorId == maghrib.Id && !item.IsCancelled && item.StartTime is not null)
            .Select(item => (DateOnly?)item.Date).Max();
        if (lastMaghrib is null || lastMaghrib < _organizationToday.AddDays(30))
            return lastMaghrib is null
                ? "Maghrib times haven't been entered. Add them on the Prayer times screen."
                : $"Prayer times are only entered up to {lastMaghrib:dd MMM}. Add the next month's times on the Prayer times screen.";

        OrganizationAnchor? zuhr = prayers.Anchors.FirstOrDefault(item => item.Key == "zuhr");
        bool hasJumuah = zuhr is not null && prayers.StandingTimes.Any(item =>
            item.AnchorId == zuhr.Id && item.Weekday == 4 && item.DurationMinutes is not null
            && item.EffectiveFrom <= _organizationToday);
        if (!hasJumuah)
            return "Friday Jumu'ah time isn't set, so Friday lessons can't be adjusted. Add it on the Prayer times screen.";

        foreach (string key in new[] { "asr", "isha" })
        {
            OrganizationAnchor? anchor = prayers.Anchors.FirstOrDefault(item => item.Key == key);
            if (anchor is null || !prayers.StandingTimes.Any(item => item.AnchorId == anchor.Id
                    && item.Weekday is null && item.EffectiveFrom <= _organizationToday))
                return $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key)} time isn't set. Add it on the Prayer times screen.";
        }
        if (run?.Error is not null) return run.Error;
        return IsMaintenanceOverdue
            ? "Lesson times have not been adjusted in the last 48 hours and may be out of date."
            : null;
    }

    public async Task RegenerateOnAdminEntryAsync(CancellationToken token = default)
    {
        try
        {
            GeneratorMaintenanceRun run = await _gateway.RegenerateGeneratedTimetablesAsync(token);
            LastGeneratorRunAt = run.StartedAt; OnPropertyChanged(nameof(IsMaintenanceOverdue));
            if (run.TimetablesWritten > 0) await _sync.SyncTableAsync(CacheTable.Periods, token);
            if (IsAutomatic)
                await RefreshAutomationWarningAsync(AdjustsForPrayer, run, token);
            else
                AutomationMessage = run.Error ?? (IsMaintenanceOverdue ? "Lesson times have not been adjusted in the last 48 hours and may be out of date." : null);
            await RefreshClashWarningsAsync(token);
            IsAutomationReadUnavailable = false;
        }
        catch (NotSupportedException) { }
        catch (HttpRequestException) { DegradeAutomationForOfflineRead(); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { DegradeAutomationForOfflineRead(); }
        catch (ServerWriteException ex) { AutomationMessage = ex.Message; }
    }

    public void Receive(DataChanged message)
    {
        if (message.Table is not (CacheTable.Timetables or CacheTable.Periods) || _ownWriteDepth > 0) return;
        UiDispatch.Run(() => { if (IsDirty) HasConflict = true; else _ = LoadAsync(); });
    }

    public void Receive(PrayerTimesChanged message) => UiDispatch.Run(() =>
    {
        if (Selected is not null && IsAutomatic) _ = LoadAutomationAsync(Selected);
    });

    public void Dispose()
    {
        _previewDebounce?.Cancel();
        _previewDebounce?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public partial class PrayerFixedTimeEditorItem : ObservableObject
{
    public required string AnchorKey { get; init; }
    public required string Label { get; init; }
    public int? Weekday { get; init; }
    [ObservableProperty] private TimeSpan _start;
    [ObservableProperty] private int? _durationMinutes = TimetableGenerator.PrayerMinutes;
}

public partial class PrayerTimesViewModel(ISupabaseGateway gateway, IMessenger? messenger = null) : ObservableObject
{
    private PrayerTimesSnapshot? _snapshot;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    public ObservableCollection<PrayerFixedTimeEditorItem> FixedTimes { get; } = [];
    public ObservableCollection<AnchorDateOverrideWrite> MaghribPreview { get; } = [];
    [ObservableProperty] private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _effectiveFrom = DateTime.Today;
    [ObservableProperty] private string _maghribText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string? _fixedTimesMessage;
    [ObservableProperty] private bool _isBusy;

    public async Task LoadAsync(CancellationToken token = default)
    {
        IsBusy = true;
        try
        {
            try { _today = await gateway.GetCurrentOrganizationDateAsync(token); }
            catch (NotSupportedException) { _today = DateOnly.FromDateTime(DateTime.Today); }
            if (EffectiveFrom.Date < _today.ToDateTime(TimeOnly.MinValue)) EffectiveFrom = _today.ToDateTime(TimeOnly.MinValue);
            _snapshot = await gateway.GetPrayerTimesAsync(DateOnly.FromDateTime(Month), token);
            PopulateFixedTimes();
            Message = null; FixedTimesMessage = null;
        }
        catch (NotSupportedException) { Message = "Prayer-time editing requires the v0.15 server update."; }
        catch (ServerWriteException ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task LoadMonthAsync(CancellationToken token) => LoadAsync(token);

    private void PopulateFixedTimes()
    {
        FixedTimes.Clear();
        AddFixed("asr", "Asr", null, new(17, 0, 0), 10);
        AddFixed("isha", "Isha", null, new(20, 15, 0), 10);
        AddFixed("zuhr", "Friday Jumu'ah", 4, new(13, 0, 0), null);
    }

    private void AddFixed(string key, string label, int? weekday, TimeSpan fallback, int? fallbackDuration)
    {
        OrganizationAnchor? anchor = _snapshot?.Anchors.FirstOrDefault(item => item.Key == key);
        AnchorStandingTime? current = anchor is null ? null : _snapshot!.StandingTimes
            .Where(item => item.AnchorId == anchor.Id && item.Weekday == weekday && item.EffectiveFrom <= DateOnly.FromDateTime(EffectiveFrom))
            .OrderByDescending(item => item.EffectiveFrom).FirstOrDefault();
        FixedTimes.Add(new() { AnchorKey = key, Label = label, Weekday = weekday,
            Start = current?.StartTime.ToTimeSpan() ?? fallback, DurationMinutes = current?.DurationMinutes ?? fallbackDuration });
    }

    [RelayCommand]
    private void CheckMaghrib()
    {
        MaghribPreview.Clear(); Message = null;
        string[] rows = MaghribText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int days = DateTime.DaysInMonth(Month.Year, Month.Month);
        if (rows.Length != days) { Message = $"Paste exactly {days} times, one for each day."; return; }
        PrayerTimesSnapshot? snapshot = _snapshot;
        OrganizationAnchor? maghrib = snapshot?.Anchors.FirstOrDefault(item => item.Key == "maghrib");
        if (maghrib is null) { Message = "The Maghrib prayer configuration is unavailable."; return; }
        int unchanged = 0;
        for (int day = 1; day <= days; day++)
        {
            if (!TimeOnly.TryParseExact(rows[day - 1], ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            { MaghribPreview.Clear(); Message = $"Day {day} isn't a valid time (HH:mm)."; return; }
            DateOnly date = new(Month.Year, Month.Month, day);
            AnchorDateOverride? existing = snapshot!.DateOverrides.FirstOrDefault(item => item.AnchorId == maghrib.Id && item.Date == date);
            if (existing?.StartTime == time && existing.IsCancelled == false) unchanged++;
            else MaghribPreview.Add(new(date, time, TimetableGenerator.PrayerMinutes));
        }
        Message = $"{days} times read. {MaghribPreview.Count} will change, {unchanged} already match.";
    }

    [RelayCommand]
    private async Task SaveMaghribAsync(CancellationToken token)
    {
        CheckMaghrib();
        if (Message is null || Message.StartsWith("Paste ", StringComparison.Ordinal) || Message.Contains("isn't", StringComparison.Ordinal) || Message.Contains("unavailable", StringComparison.Ordinal)) return;
        OrganizationAnchor? maghrib = _snapshot?.Anchors.First(item => item.Key == "maghrib");
        if (maghrib is null) return;
        try
        {
            int changed = await gateway.BulkUpsertAnchorDateOverridesAsync(maghrib.Id, MaghribPreview.ToArray(), token);
            Message = $"Saved {changed} Maghrib times atomically.";
            _snapshot = await gateway.GetPrayerTimesAsync(DateOnly.FromDateTime(Month), token);
            messenger?.Send(new PrayerTimesChanged());
        }
        catch (ServerWriteException ex) { Message = ex.Message; }
    }

    [RelayCommand]
    private async Task SaveFixedTimesAsync(CancellationToken token)
    {
        DateOnly effective = DateOnly.FromDateTime(EffectiveFrom);
        if (effective < _today) { FixedTimesMessage = "Choose today or a future date."; return; }
        if (FixedTimes.Any(item => item.Start < TimeSpan.Zero || item.Start > new TimeSpan(23, 59, 0)))
        { FixedTimesMessage = "Enter each prayer time as HH:mm."; return; }
        if (FixedTimes.Any(item => item.DurationMinutes is not null and (< 1 or > 120)))
        { FixedTimesMessage = "Prayer lengths must be between 1 and 120 minutes."; return; }
        try
        {
            PrayerFixedTimeWrite[] rows = FixedTimes.Select(item => new PrayerFixedTimeWrite(
                item.AnchorKey, TimeOnly.FromTimeSpan(item.Start), item.DurationMinutes, item.Weekday)).ToArray();
            int changed = await gateway.SavePrayerFixedTimesAsync(rows, effective, token);
            FixedTimesMessage = $"Saved {changed} fixed prayer times.";
            _snapshot = await gateway.GetPrayerTimesAsync(DateOnly.FromDateTime(Month), token);
            messenger?.Send(new PrayerTimesChanged());
        }
        catch (ServerWriteException ex) { FixedTimesMessage = ex.Message; }
    }
}
