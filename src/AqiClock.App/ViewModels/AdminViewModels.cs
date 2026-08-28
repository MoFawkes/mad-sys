using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.App.Services;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using QRCoder;

namespace AqiClock.App.ViewModels;

public partial class AdminViewModel : ObservableObject, IRecipient<SessionChanged>, IRecipient<ConnectivityChanged>
{
    private readonly IWindowService _windows;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _isEditable;
    [ObservableProperty] private string? _banner;
    private string? _roleBanner;
    private string? _offlineBanner;
    public bool HasBanner => !string.IsNullOrWhiteSpace(Banner);
    public TimetableEditorViewModel Timetables { get; }
    public WeekScheduleViewModel WeekSchedule { get; }
    public OverridesViewModel Overrides { get; }
    public AnnouncementComposeViewModel Announcements { get; }
    public AuditViewModel Audit { get; }
    public UsersViewModel Users { get; }
    public ClassesViewModel? Classes { get; }
    public StudentDevicesViewModel? StudentDevices { get; }

    public AdminViewModel(TimetableEditorViewModel timetables, WeekScheduleViewModel weekSchedule, OverridesViewModel overrides, AnnouncementComposeViewModel announcements, AuditViewModel audit, UsersViewModel users, ISyncService sync, IWindowService windows, IMessenger messenger)
    {
        Timetables = timetables; WeekSchedule = weekSchedule; Overrides = overrides; Announcements = announcements; Audit = audit; Users = users; _windows = windows; InitializeConnectivity(sync.State);
        messenger.Register<SessionChanged>(this); messenger.Register<ConnectivityChanged>(this);
    }

    public AdminViewModel(TimetableEditorViewModel timetables, WeekScheduleViewModel weekSchedule, OverridesViewModel overrides, AnnouncementComposeViewModel announcements, AuditViewModel audit, UsersViewModel users, ClassesViewModel classes, ISyncService sync, IWindowService windows, IMessenger messenger)
    {
        Timetables = timetables; WeekSchedule = weekSchedule; Overrides = overrides; Announcements = announcements; Audit = audit; Users = users; Classes = classes; _windows = windows; InitializeConnectivity(sync.State);
        messenger.Register<SessionChanged>(this); messenger.Register<ConnectivityChanged>(this);
    }

    public AdminViewModel(TimetableEditorViewModel timetables, WeekScheduleViewModel weekSchedule, OverridesViewModel overrides, AnnouncementComposeViewModel announcements, AuditViewModel audit, UsersViewModel users, ClassesViewModel classes, StudentDevicesViewModel studentDevices, ISyncService sync, IWindowService windows, IMessenger messenger)
    {
        Timetables = timetables; WeekSchedule = weekSchedule; Overrides = overrides; Announcements = announcements; Audit = audit; Users = users; Classes = classes; StudentDevices = studentDevices; _windows = windows; InitializeConnectivity(sync.State);
        messenger.Register<SessionChanged>(this); messenger.Register<ConnectivityChanged>(this);
    }

    partial void OnBannerChanged(string? value) => OnPropertyChanged(nameof(HasBanner));

    public async Task InitializeAsync(CancellationToken token = default)
    {
        List<Task> tasks = [Timetables.LoadAsync(token), WeekSchedule.LoadAsync(token), Overrides.LoadAsync(token), Announcements.LoadAsync(token), Audit.LoadAsync(token), Users.LoadAsync(token)];
        if (Classes is not null) tasks.Add(Classes.LoadAsync(token));
        if (StudentDevices is not null) tasks.Add(StudentDevices.LoadAsync(token));
        await Task.WhenAll(tasks);
        if (IsOnline) await Timetables.RegenerateOnAdminEntryAsync(token);
    }
    public void Receive(SessionChanged message) => UiDispatch.Run(() =>
    {
        if (message.State.Role == UserRole.Admin) _roleBanner = null;
        else if (message.State.RoleConfirmed) _roleBanner = "Your role changed. The admin editor has been closed.";
        else return;
        UpdateBanner();
    });

    public void ResetTransientState()
    {
        _roleBanner = null;
        UpdateBanner();
    }

    public void Receive(ConnectivityChanged message) => UiDispatch.Run(() =>
    {
        IsOnline = message.State == ConnectivityState.Online;
        IsEditable = message.State != ConnectivityState.Offline;
        _offlineBanner = message.State == ConnectivityState.Offline ? "Editing is unavailable while offline." : null;
        UpdateBanner();
        if (message.State == ConnectivityState.Online) _ = Timetables.RegenerateOnAdminEntryAsync();
    });

    private void InitializeConnectivity(ConnectivityState state)
    {
        IsOnline = state == ConnectivityState.Online;
        IsEditable = state != ConnectivityState.Offline;
        _offlineBanner = state == ConnectivityState.Offline ? "Editing is unavailable while offline." : null;
        UpdateBanner();
    }

    private void UpdateBanner() => Banner = _roleBanner ?? _offlineBanner;

}

public partial class StudentDevicesViewModel(
    ISupabaseGateway gateway,
    IWindowService windows) : ObservableObject
{
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private BitmapImage? _qrCode;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _isBusy;

    public string DisplayCode =>
        string.Join(' ', Enumerable.Range(0, Code.Length / 4).Select(index => Code.Substring(index * 4, 4)));
    public string EnrollmentLink => string.IsNullOrEmpty(Code)
        ? string.Empty
        : $"aqiclock-mobile://student-setup?code={Code}";

    partial void OnCodeChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayCode));
        OnPropertyChanged(nameof(EnrollmentLink));
        QrCode = string.IsNullOrWhiteSpace(value) ? null : BuildQrCode(EnrollmentLink);
    }

    public async Task LoadAsync(CancellationToken token = default)
    {
        IsBusy = true;
        try
        {
            Code = await gateway.GetStudentJoinCodeAsync(token);
            Message = null;
        }
        catch (ServerDeniedException)
        {
            Message = "Your role changed.";
            windows.CloseAdminWindow("Your role changed. The admin editor has been closed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (!string.IsNullOrWhiteSpace(DisplayCode)) Clipboard.SetText(DisplayCode);
    }

    [RelayCommand]
    private void CopyLink()
    {
        if (!string.IsNullOrWhiteSpace(EnrollmentLink)) Clipboard.SetText(EnrollmentLink);
    }

    [RelayCommand]
    private async Task RotateAsync(CancellationToken token)
    {
        if (!windows.Confirm(
                "Create a new join code? Phones already enrolled will keep working. The old code will stop accepting new phones.",
                "Create new student join code"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            Code = await gateway.RotateStudentJoinCodeAsync(token);
            Message = "A new code is ready. Existing student devices remain enrolled.";
        }
        catch (ServerDeniedException)
        {
            Message = "Your role changed.";
            windows.CloseAdminWindow("Your role changed. The admin editor has been closed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RevokeAsync(CancellationToken token)
    {
        if (!windows.Confirm(
                "Remove every enrolled student device? Their clocks will stop syncing until they enrol again with the current join code.",
                "Remove all student devices"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            int removed = await gateway.RevokeStudentDevicesAsync(token);
            Message = removed == 1 ? "1 student device removed." : $"{removed} student devices removed.";
        }
        catch (ServerDeniedException)
        {
            Message = "Your role changed.";
            windows.CloseAdminWindow("Your role changed. The admin editor has been closed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static BitmapImage BuildQrCode(string content)
    {
        using QRCodeData data = QRCodeGenerator.GenerateQrCode(
            content, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        byte[] bytes = qr.GetGraphic(12, drawQuietZones: true);
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public partial class PeriodEditorItem : ObservableObject
{
    public Guid Id { get; init; }
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private TimeSpan _start;
    [ObservableProperty] private TimeSpan _end;
    [ObservableProperty] private bool _isLesson = true;
    public int SortOrder { get; set; }
}

public partial class GeneratorBlockEditorItem : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    [ObservableProperty] private string _blockKind = "lessons";
    [ObservableProperty] private string? _name;
    [ObservableProperty] private int _lessonCount = 1;
    [ObservableProperty] private int _minutes = 25;
    [ObservableProperty] private bool _hostsNaseehah;
}

public partial class AnchorChoiceEditorItem : ObservableObject
{
    public required OrganizationAnchor Anchor { get; init; }
    [ObservableProperty] private bool _isSelected;
    public string Name => Anchor.Name;
}

public partial class AnchorStandingEditorItem : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid(); public bool IsNew { get; init; }
    [ObservableProperty] private Guid _anchorId; [ObservableProperty] private int? _weekday;
    [ObservableProperty] private TimeSpan _start; [ObservableProperty] private int? _durationMinutes;
    [ObservableProperty] private DateTime _effectiveFrom = DateTime.Today; [ObservableProperty] private string? _error;
}

public partial class AnchorOverrideEditorItem : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid(); public bool IsNew { get; init; }
    [ObservableProperty] private Guid _anchorId; [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private TimeSpan? _start; [ObservableProperty] private int? _durationMinutes;
    [ObservableProperty] private bool _isCancelled; [ObservableProperty] private string? _error;
}

public sealed record ConversionDiffItem(string Change, string Current, string Generated);

public partial class TimetableEditorViewModel : ObservableObject, IRecipient<DataChanged>
{
    private readonly ISupabaseGateway _gateway; private readonly ISyncService _sync; private readonly ITimetableRepository _repository; private readonly IWeekScheduleRepository _week; private readonly IDateOverrideRepository _overrides; private readonly IWindowService _windows; private readonly IClassRepository? _classes;
    private bool _loading;
    private int _ownWriteDepth;
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
    [NotifyPropertyChangedFor(nameof(CanEditLegacyPeriods))]
    [NotifyCanExecuteChangedFor(nameof(AddPeriodCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemovePeriodCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertBreakCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShiftLaterCommand))]
    private bool _isGenerated;
    [ObservableProperty] private string _generatorSessionKind = "pm";
    [ObservableProperty] private TimeSpan _generatorDayStart = new(18, 15, 0);
    [ObservableProperty] private TimeSpan? _generatorAdvisoryEnd;
    [ObservableProperty] private string _generatorNamingPattern = "Lesson {number}";
    [ObservableProperty] private string? _generatorMessage;
    [ObservableProperty] private DateTimeOffset? _lastGeneratorRunAt;
    [ObservableProperty] private string? _maintenanceMessage;
    [ObservableProperty] private string? _clashWarningMessage;
    [ObservableProperty] private DateTime? _bulkMonth = DateTime.Today;
    [ObservableProperty] private string _maghribBulkText = string.Empty;
    [ObservableProperty] private string? _bulkMessage;
    [ObservableProperty] private string? _conversionMessage;
    [ObservableProperty] private bool _hasConversionPreview;
    public bool CanEditLegacyPeriods => !IsGenerated;
    public bool IsMaintenanceOverdue => LastGeneratorRunAt is null || DateTimeOffset.UtcNow - LastGeneratorRunAt > TimeSpan.FromHours(48);
    public ObservableCollection<Timetable> Items { get; } = [];
    public ObservableCollection<PeriodEditorItem> Periods { get; } = [];
    public ObservableCollection<GeneratorBlockEditorItem> GeneratorBlocks { get; } = [];
    public ObservableCollection<AnchorChoiceEditorItem> GeneratorAnchors { get; } = [];
    public ObservableCollection<PeriodEditorItem> GeneratorPreview { get; } = [];
    public ObservableCollection<AnchorStandingEditorItem> StandingTimes { get; } = [];
    public ObservableCollection<AnchorOverrideEditorItem> AnchorDateOverrides { get; } = [];
    public ObservableCollection<AnchorDateOverrideWrite> BulkPreview { get; } = [];
    public ObservableCollection<ConversionDiffItem> ConversionDiff { get; } = [];
    private AnchorConfigurationSnapshot? _anchorConfiguration;
    private GeneratorServerPreview? _pendingConversionPreview;
    private DateOnly _previewDate = DateOnly.FromDateTime(DateTime.Today);

    public TimetableEditorViewModel(ISupabaseGateway gateway, ISyncService sync, ITimetableRepository repository, IWeekScheduleRepository week, IDateOverrideRepository overrides, IWindowService windows, IMessenger messenger)
    { _gateway = gateway; _sync = sync; _repository = repository; _week = week; _overrides = overrides; _windows = windows; Periods.CollectionChanged += OnPeriodsChanged; GeneratorBlocks.CollectionChanged += OnGeneratorRowsChanged; GeneratorAnchors.CollectionChanged += OnGeneratorRowsChanged; messenger.Register(this); }

    public TimetableEditorViewModel(ISupabaseGateway gateway, ISyncService sync, ITimetableRepository repository, IWeekScheduleRepository week, IDateOverrideRepository overrides, IClassRepository classes, IWindowService windows, IMessenger messenger)
        : this(gateway, sync, repository, week, overrides, windows, messenger) => _classes = classes;

    public async Task LoadAsync(CancellationToken token = default)
    {
        Guid? selectedId = Selected?.Id;
        IReadOnlyList<Timetable> rows = await _repository.GetAllAsync(token);
        _loading = true;
        Items.Clear();
        foreach (Timetable row in rows.OrderBy(x => x.Name)) Items.Add(row);
        _loading = false;
        Timetable? target = selectedId is { } id ? Items.FirstOrDefault(x => x.Id == id) : Items.FirstOrDefault();
        Selected = target;
        if (target is not null) { Select(target); await LoadGeneratorAsync(target, token); }
    }

    partial void OnSelectedChanged(Timetable? value) { if (value is not null) { Select(value); _ = LoadGeneratorAsync(value); } }
    private void Select(Timetable value) { _loading = true; Name = value.Name; IsArchived = value.IsArchived; DetachPeriodHandlers(); Periods.Clear(); foreach (Period p in value.Periods.OrderBy(x => x.SortOrder)) Periods.Add(new() { Id = p.Id, Name = p.Name, Start = p.StartTime.ToTimeSpan(), End = p.EndTime.ToTimeSpan(), IsLesson = p.IsLesson, SortOrder = p.SortOrder }); IsDirty = false; HasConflict = false; ValidationMessage = null; _loading = false; }

    /// <summary>Clear() raises a Reset with no OldItems, so discarded rows must be detached here or they keep marking the editor dirty.</summary>
    private void DetachPeriodHandlers() { foreach (PeriodEditorItem item in Periods) item.PropertyChanged -= OnPeriodChanged; }
    partial void OnNameChanged(string value) { if (!_loading) IsDirty = true; }
    partial void OnIsArchivedChanged(bool value) { if (!_loading) IsDirty = true; }
    private void OnPeriodsChanged(object? sender, NotifyCollectionChangedEventArgs args) { if (args.OldItems is not null) foreach (PeriodEditorItem item in args.OldItems) item.PropertyChanged -= OnPeriodChanged; if (args.NewItems is not null) foreach (PeriodEditorItem item in args.NewItems) item.PropertyChanged += OnPeriodChanged; if (!_loading) IsDirty = true; }
    private void OnPeriodChanged(object? sender, PropertyChangedEventArgs args) { if (!_loading) IsDirty = true; }

    [RelayCommand] private void NewTimetable() { Selected = new Timetable(Guid.NewGuid(), "New timetable", false, []); IsDirty = true; }
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
        {
            ValidationMessage = "That break would cross midnight or leave an invalid period boundary.";
            return;
        }

        var inserted = new PeriodEditorItem
        {
            Id = Guid.NewGuid(),
            Name = UniquePeriodName(BreakName.Trim()),
            Start = start,
            End = end,
            IsLesson = false,
        };
        _loading = true;
        try
        {
            Periods.Insert(shiftIndex, inserted);
            ApplyShift(shifted);
            SelectedPeriod = inserted;
        }
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
        try
        {
            ApplyShift(shifted);
            if (index > 0) Periods[index - 1].End = shifted[0].Start;
        }
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
        {
            ValidationMessage = "That shift would move a period outside 00:00–23:59.";
            return false;
        }
        if (validateSeam && index > 0 && shifted.Length > 0 && shifted[0].Item2 <= Periods[index - 1].Start)
        {
            ValidationMessage = "That shift would leave the preceding period with an invalid end time.";
            return false;
        }
        return true;
    }

    private static bool IsMinuteWithinDay(TimeSpan value) => SchedulingValueRules.IsMinuteWithinDay(value);
    private static void ApplyShift(IEnumerable<(PeriodEditorItem Item, TimeSpan Start, TimeSpan End)> shifted)
    {
        foreach (var item in shifted) { item.Item.Start = item.Start; item.Item.End = item.End; }
    }
    private string UniquePeriodName(string requested)
    {
        return SchedulingValueRules.UniquePeriodName(requested, Periods.Select(item => item.Name));
    }
    [RelayCommand] private void MarkDirty() => IsDirty = true;
    [RelayCommand] private void Cancel() { if (Selected is not null) Select(Selected); }
    [RelayCommand]
    private async Task ReloadAsync(CancellationToken token)
    {
        HasConflict = false;
        IsDirty = false;
        await LoadAsync(token);
    }
    [RelayCommand] private void Overwrite() => HasConflict = false;

    [RelayCommand]
    private async Task SaveAsync(CancellationToken token)
    {
        if (Selected is null) { ValidationMessage = "Select or create a timetable before saving."; return; }
        if (!Validate()) return;
        _ownWriteDepth++;
        try
        {
            Guid org = await _gateway.GetCurrentOrganizationIdAsync(token);
            var row = new TimetableRow(Selected.Id, org, Name.Trim(), IsArchived);
            var periods = new List<PeriodRow>(Periods.Count);
            for (int index = 0; index < Periods.Count; index++)
            {
                PeriodEditorItem p = Periods[index];
                periods.Add(new PeriodRow(p.Id, Selected.Id, p.Name.Trim(), TimeOnly.FromTimeSpan(p.Start), TimeOnly.FromTimeSpan(p.End), index, p.IsLesson));
            }
            await _gateway.SaveTimetableAsync(row, periods, token);
            await _sync.SyncTableAsync(CacheTable.Timetables, token); await _sync.SyncTableAsync(CacheTable.Periods, token); await LoadAsync(token); Timetable? saved = Items.FirstOrDefault(x => x.Id == row.Id); Selected = saved; if (saved is not null) Select(saved); IsDirty = false; HasConflict = false;
        }
        catch (DuplicateRowException) { ValidationMessage = "A timetable or period name is already used."; }
        catch (ServerDeniedException) { ValidationMessage = "Your role changed."; _windows.CloseAdminWindow(); }
        catch (ServerWriteException ex) { ValidationMessage = ex.Message; }
        finally { _ownWriteDepth--; }
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken token)
    {
        if (Selected is null) return;
        List<string> used = [];
        WeekSchedule week = await _week.GetAsync(token);
        Dictionary<Guid, string> classNames = _classes is null
            ? new Dictionary<Guid, string>()
            : (await _classes.GetAllAsync(token)).ToDictionary(item => item.Id, item => item.Name);
        foreach (WeekScheduleEntry entry in week.AllEntries.Where(entry => entry.TimetableId == Selected.Id))
        {
            string qualifier = entry.AudienceClassId is { } classId && classNames.TryGetValue(classId, out string? className)
                ? $" ({className})"
                : entry.AudienceClassId is not null ? " (class-specific)" : string.Empty;
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

    public bool Validate()
    {
        ValidationMessage = null; WarningMessage = null;
        if (string.IsNullOrWhiteSpace(Name)) { ValidationMessage = "Timetable name is required."; return false; }
        if (Items.Any(x => x.Id != Selected?.Id && string.Equals(x.Name, Name.Trim(), StringComparison.OrdinalIgnoreCase))) { ValidationMessage = "A timetable with this name already exists."; return false; }
        if (Periods.Any(x => x.End <= x.Start)) { ValidationMessage = "Every period must end after it starts."; return false; }
        if (Periods.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) { ValidationMessage = "Period names must be unique within a timetable."; return false; }
        PeriodEditorItem[] ordered = Periods.OrderBy(x => x.Start).ToArray(); if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.End > pair.Second.Start)) WarningMessage = "Some periods overlap. Saving is allowed.";
        return true;
    }

    private async Task LoadGeneratorAsync(Timetable timetable, CancellationToken token = default)
    {
        try
        {
            _loading = true;
            GeneratorAuthoringSnapshot authoring = await _gateway.GetGeneratorAuthoringAsync(timetable.Id, token);
            _anchorConfiguration ??= await _gateway.GetAnchorConfigurationAsync(token);
            try { _previewDate = await _gateway.GetCurrentOrganizationDateAsync(token); }
            catch (NotSupportedException) { }
            GeneratorBlocks.Clear(); GeneratorAnchors.Clear(); StandingTimes.Clear(); AnchorDateOverrides.Clear();
            foreach (AnchorStandingTime row in _anchorConfiguration.StandingTimes) StandingTimes.Add(new() { Id = row.Id, AnchorId = row.AnchorId, Weekday = row.Weekday, Start = row.StartTime.ToTimeSpan(), DurationMinutes = row.DurationMinutes, EffectiveFrom = row.EffectiveFrom.ToDateTime(TimeOnly.MinValue) });
            foreach (AnchorDateOverride row in _anchorConfiguration.DateOverrides) AnchorDateOverrides.Add(new() { Id = row.Id, AnchorId = row.AnchorId, Date = row.Date.ToDateTime(TimeOnly.MinValue), Start = row.StartTime?.ToTimeSpan(), DurationMinutes = row.DurationMinutes, IsCancelled = row.IsCancelled });
            foreach (OrganizationAnchor anchor in _anchorConfiguration.Anchors)
                GeneratorAnchors.Add(new() { Anchor = anchor, IsSelected = authoring.Anchors.Any(x => x.AnchorId == anchor.Id) });
            if (authoring.Definition is { } definition)
            {
                IsGenerated = true;
                GeneratorSessionKind = definition.SessionKind;
                GeneratorDayStart = definition.DayStart.ToTimeSpan();
                GeneratorAdvisoryEnd = definition.AdvisoryDayEnd?.ToTimeSpan();
                GeneratorNamingPattern = definition.NamingPattern;
                foreach (TimetableGeneratorBlock block in authoring.Blocks)
                    GeneratorBlocks.Add(new() { Id = block.Id, BlockKind = block.BlockKind, Name = block.Name,
                        LessonCount = block.LessonCount ?? 1, Minutes = block.LessonMinutes ?? block.BreakMinutes ?? 1,
                        HostsNaseehah = block.HostsNaseehah });
            }
            else IsGenerated = false;
            RefreshGeneratorPreview();
            GeneratorMaintenanceRun? run = await _gateway.GetLatestGeneratorMaintenanceRunAsync(token);
            LastGeneratorRunAt = run?.StartedAt;
            OnPropertyChanged(nameof(IsMaintenanceOverdue));
            MaintenanceMessage = run?.Error ?? (IsMaintenanceOverdue ? "Generator maintenance has not succeeded in the last 48 hours." : null);
            await RefreshClashWarningsAsync(token);
            IsDirty = false;
        }
        catch (NotSupportedException) { IsGenerated = false; }
        catch (ServerWriteException ex) { GeneratorMessage = ex.Message; }
        finally { _loading = false; }
    }

    private void OnGeneratorRowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null) foreach (ObservableObject item in args.OldItems) item.PropertyChanged -= OnGeneratorRowChanged;
        if (args.NewItems is not null) foreach (ObservableObject item in args.NewItems) item.PropertyChanged += OnGeneratorRowChanged;
        if (!_loading) IsDirty = true;
        RefreshGeneratorPreview();
    }
    private void OnGeneratorRowChanged(object? sender, PropertyChangedEventArgs args) { if (!_loading) IsDirty = true; RefreshGeneratorPreview(); }
    partial void OnGeneratorSessionKindChanged(string value) { if (!_loading) IsDirty = true; RefreshGeneratorPreview(); }
    partial void OnGeneratorDayStartChanged(TimeSpan value) { if (!_loading) IsDirty = true; RefreshGeneratorPreview(); }
    partial void OnGeneratorAdvisoryEndChanged(TimeSpan? value) { if (!_loading) IsDirty = true; RefreshGeneratorPreview(); }
    partial void OnGeneratorNamingPatternChanged(string value) { if (!_loading) IsDirty = true; RefreshGeneratorPreview(); }

    [RelayCommand] private void AddGeneratorBlock() => GeneratorBlocks.Add(new());
    [RelayCommand] private void RemoveGeneratorBlock(GeneratorBlockEditorItem item) => GeneratorBlocks.Remove(item);
    [RelayCommand]
    private void RefreshGeneratorPreview()
    {
        if (_anchorConfiguration is null || Selected is null) return;
        try
        {
            DateOnly date = _previewDate;
            var blocks = GeneratorBlocks.Select(item => new GeneratorBlock(item.Id,
                item.BlockKind == "break" ? GeneratorBlockKind.Break : GeneratorBlockKind.Lessons,
                item.Name ?? string.Empty, item.BlockKind == "break" ? 1 : item.LessonCount,
                item.Minutes, item.HostsNaseehah)).ToArray();
            var anchors = GeneratorAnchors.Where(item => item.IsSelected)
                .Select(item => ResolveAnchor(item.Anchor, date)).Where(item => item is not null).Cast<ResolvedAnchor>().ToArray();
            GeneratorResult result = AlQalamExpansionRules.Expand(Selected.Id,
                string.Equals(GeneratorSessionKind, "am", StringComparison.OrdinalIgnoreCase) ? AqiClock.Domain.Scheduling.GeneratorSessionKind.Am : AqiClock.Domain.Scheduling.GeneratorSessionKind.Pm,
                TimeOnly.FromTimeSpan(GeneratorDayStart), blocks, anchors,
                GeneratorAdvisoryEnd is { } advisory ? TimeOnly.FromTimeSpan(advisory) : null,
                GeneratorNamingPattern);
            GeneratorPreview.Clear();
            for (int index = 0; index < result.Periods.Count; index++)
            {
                GeneratedPeriod period = result.Periods[index];
                GeneratorPreview.Add(new() { Id = period.Id, Name = period.Name, Start = period.Start.ToTimeSpan(), End = period.End.ToTimeSpan(), IsLesson = period.IsLesson, SortOrder = index });
            }
            GeneratorMessage = result.Warnings.Count == 0 ? null : string.Join(" ", result.Warnings.Select(x => x.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { GeneratorMessage = ex.Message; GeneratorPreview.Clear(); }
    }

    private GeneratorDefinitionWrite CurrentGeneratorDefinition() => new(
        GeneratorSessionKind, TimeOnly.FromTimeSpan(GeneratorDayStart),
        GeneratorAdvisoryEnd is { } end ? TimeOnly.FromTimeSpan(end) : null,
        GeneratorNamingPattern);

    private GeneratorBlockWrite[] CurrentGeneratorBlocks() => GeneratorBlocks.Select((item, index) =>
        new GeneratorBlockWrite(item.Id, index, item.BlockKind, item.Name,
            item.BlockKind == "lessons" ? item.LessonCount : null,
            item.BlockKind == "lessons" ? item.Minutes : null,
            item.BlockKind == "break" ? item.Minutes : null, item.HostsNaseehah)).ToArray();

    private Guid[] CurrentGeneratorAnchorIds() => GeneratorAnchors.Where(x => x.IsSelected)
        .Select(x => x.Anchor.Id).ToArray();

    private async Task<GeneratorServerPreview> RefreshServerGeneratorPreviewAsync(CancellationToken token)
    {
        if (Selected is null) throw new InvalidOperationException("Select a timetable first.");
        GeneratorServerPreview preview = await _gateway.PreviewGeneratedTimetableAsync(
            Selected.Id, CurrentGeneratorDefinition(), CurrentGeneratorBlocks(), CurrentGeneratorAnchorIds(), token);
        _previewDate = preview.Date;
        GeneratorPreview.Clear();
        foreach (PeriodRow period in preview.Periods.OrderBy(x => x.SortOrder))
            GeneratorPreview.Add(new() { Id = period.Id, Name = period.Name, Start = period.StartTime.ToTimeSpan(),
                End = period.EndTime.ToTimeSpan(), IsLesson = period.IsLesson, SortOrder = period.SortOrder });
        return preview;
    }

    private ResolvedAnchor? ResolveAnchor(OrganizationAnchor anchor, DateOnly date)
    {
        AnchorDateOverride? dateRow = _anchorConfiguration!.DateOverrides.FirstOrDefault(x => x.AnchorId == anchor.Id && x.Date == date);
        if (dateRow?.IsCancelled == true) return null;
        if (dateRow is not null) return new(anchor.Id, anchor.Key, anchor.Name, dateRow.StartTime!.Value, dateRow.DurationMinutes);
        int weekday = ((int)date.DayOfWeek + 6) % 7;
        AnchorStandingTime? row = _anchorConfiguration.StandingTimes
            .Where(x => x.AnchorId == anchor.Id && x.EffectiveFrom <= date && x.Weekday == weekday)
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefault()
            ?? _anchorConfiguration.StandingTimes.Where(x => x.AnchorId == anchor.Id && x.EffectiveFrom <= date && x.Weekday is null)
                .OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
        return row is null ? null : new(anchor.Id, anchor.Key, anchor.Name, row.StartTime, row.DurationMinutes);
    }

    [RelayCommand]
    private async Task SaveGeneratorAsync(CancellationToken token)
    {
        if (Selected is null || GeneratorBlocks.Count == 0) { GeneratorMessage = "Create a valid preview before saving."; return; }
        try
        {
            PeriodRow[] clientPreview = GeneratorPreview.Select((item, index) =>
                new PeriodRow(item.Id, Selected.Id, item.Name, TimeOnly.FromTimeSpan(item.Start),
                    TimeOnly.FromTimeSpan(item.End), index, item.IsLesson)).ToArray();
            GeneratorServerPreview preview = await RefreshServerGeneratorPreviewAsync(token);
            if (!clientPreview.SequenceEqual(preview.Periods))
            {
                GeneratorMessage = $"The server preview for {preview.Date:yyyy-MM-dd} differs from the preview you reviewed. " +
                    "The server version is now shown; review it and click Save generator again to accept it.";
                return;
            }
            await _gateway.SaveGeneratedTimetableAsync(Selected.Id, CurrentGeneratorDefinition(), CurrentGeneratorBlocks(),
                CurrentGeneratorAnchorIds(), preview.Periods, token);
            IsGenerated = true; IsDirty = false; GeneratorMessage = "Generator saved.";
        }
        catch (ServerWriteException ex) { GeneratorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task PreviewConversionAsync(CancellationToken token)
    {
        ConversionDiff.Clear(); HasConversionPreview = false; _pendingConversionPreview = null;
        if (Selected is null || IsGenerated) { ConversionMessage = "Select a legacy timetable to convert."; return; }
        if (!TryInferLegacyDefinition(out string? failure)) { ConversionMessage = failure; return; }
        try
        {
            GeneratorServerPreview preview = await RefreshServerGeneratorPreviewAsync(token);
            _pendingConversionPreview = preview;
            BuildConversionDiff(preview.Periods);
            HasConversionPreview = true;
            ConversionMessage = $"Server preview for {preview.Date:yyyy-MM-dd}: {ConversionDiff.Count} change(s). Review before converting.";
        }
        catch (ServerWriteException ex) { ConversionMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task ConfirmConversionAsync(CancellationToken token)
    {
        if (Selected is null || _pendingConversionPreview is null || !HasConversionPreview) return;
        if (!_windows.Confirm("Convert this timetable using the server preview shown? This is one-way.", "Convert to generated")) return;
        try
        {
            await _gateway.SaveGeneratedTimetableAsync(Selected.Id, CurrentGeneratorDefinition(), CurrentGeneratorBlocks(),
                CurrentGeneratorAnchorIds(), _pendingConversionPreview.Periods, token);
            IsGenerated = true; IsDirty = false; HasConversionPreview = false;
            ConversionMessage = "Timetable converted to generated.";
        }
        catch (ServerWriteException ex) { ConversionMessage = ex.Message; }
    }

    private bool TryInferLegacyDefinition(out string? failure)
    {
        failure = null;
        PeriodEditorItem[] rows = Periods.OrderBy(x => x.SortOrder).ToArray();
        if (rows.Length == 0) { failure = "Cannot infer blocks: the timetable has no periods."; return false; }
        for (int index = 1; index < rows.Length; index++)
            if (rows[index - 1].End != rows[index].Start)
            { failure = $"Cannot infer blocks: '{rows[index - 1].Name}' and '{rows[index].Name}' are not contiguous."; return false; }
        OrganizationAnchor? anchorLike = _anchorConfiguration?.Anchors.FirstOrDefault(anchor => rows.Any(row =>
            string.Equals(row.Name, anchor.Name, StringComparison.OrdinalIgnoreCase) ||
            row.Name.StartsWith(anchor.Name + " +", StringComparison.OrdinalIgnoreCase)));
        if (anchorLike is not null)
        { failure = $"Cannot infer anchor placement for '{anchorLike.Name}' safely; build this generator explicitly."; return false; }

        PeriodEditorItem[] lessons = rows.Where(x => x.IsLesson).ToArray();
        if (lessons.Length == 0) { failure = "Cannot infer a lesson naming pattern: no lesson rows exist."; return false; }
        Match first = Regex.Match(lessons[0].Name, "^(.*?)(\\d+)([^\\d]*)$", RegexOptions.CultureInvariant);
        if (!first.Success) { failure = $"Cannot infer a lesson number from '{lessons[0].Name}'."; return false; }
        string prefix = first.Groups[1].Value; string suffix = first.Groups[3].Value;
        for (int index = 0; index < lessons.Length; index++)
        {
            Match match = Regex.Match(lessons[index].Name, "^(.*?)(\\d+)([^\\d]*)$", RegexOptions.CultureInvariant);
            if (!match.Success || match.Groups[1].Value != prefix || match.Groups[3].Value != suffix ||
                !int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out int number) || number != index + 1)
            { failure = $"Cannot infer one sequential naming pattern at '{lessons[index].Name}'."; return false; }
        }

        _loading = true;
        try
        {
            GeneratorBlocks.Clear();
            for (int index = 0; index < rows.Length;)
            {
                PeriodEditorItem row = rows[index];
                int minutes = checked((int)(row.End - row.Start).TotalMinutes);
                if (minutes <= 0) { failure = $"Cannot infer a positive duration for '{row.Name}'."; return false; }
                if (!row.IsLesson)
                {
                    GeneratorBlocks.Add(new() { BlockKind = "break", Name = row.Name, LessonCount = 1, Minutes = minutes });
                    index++; continue;
                }
                int count = 1;
                while (index + count < rows.Length && rows[index + count].IsLesson &&
                       rows[index + count].End - rows[index + count].Start == row.End - row.Start) count++;
                GeneratorBlocks.Add(new() { BlockKind = "lessons", LessonCount = count, Minutes = minutes });
                index += count;
            }
            foreach (AnchorChoiceEditorItem anchor in GeneratorAnchors) anchor.IsSelected = false;
            GeneratorDayStart = rows[0].Start;
            GeneratorSessionKind = rows[0].Start.Hours < 15 ? "am" : "pm";
            GeneratorNamingPattern = prefix + "{number}" + suffix;
        }
        catch (OverflowException) { failure = "Cannot infer blocks: a period duration is outside the supported range."; return false; }
        finally { _loading = false; }
        IsDirty = true; RefreshGeneratorPreview();
        return true;
    }

    private void BuildConversionDiff(IReadOnlyList<PeriodRow> generated)
    {
        int count = Math.Max(Periods.Count, generated.Count);
        for (int index = 0; index < count; index++)
        {
            PeriodEditorItem? before = index < Periods.Count ? Periods[index] : null;
            PeriodRow? after = index < generated.Count ? generated[index] : null;
            string current = before is null ? "—" : $"{before.Name} {before.Start:hh\\:mm}–{before.End:hh\\:mm}";
            string next = after is null ? "—" : $"{after.Name} {after.StartTime:HH:mm}–{after.EndTime:HH:mm}";
            if (current != next || before?.Id != after?.Id)
                ConversionDiff.Add(new(before is null ? "Add" : after is null ? "Remove" : "Change", current, next));
        }
    }

    [RelayCommand]
    private void PreviewMaghribBulkPaste()
    {
        BulkPreview.Clear(); BulkMessage = null;
        if (BulkMonth is null) { BulkMessage = "Choose a month."; return; }
        string[] rows = MaghribBulkText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int days = DateTime.DaysInMonth(BulkMonth.Value.Year, BulkMonth.Value.Month);
        if (rows.Length != days) { BulkMessage = $"Paste exactly {days} times, one for each day."; return; }
        for (int day = 1; day <= days; day++)
        {
            if (!TimeOnly.TryParseExact(rows[day - 1], ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            { BulkPreview.Clear(); BulkMessage = $"Day {day} is not a valid HH:mm time."; return; }
            DateOnly date = new(BulkMonth.Value.Year, BulkMonth.Value.Month, day);
            AnchorDateOverride? existing = _anchorConfiguration?.DateOverrides.FirstOrDefault(x => x.Date == date &&
                _anchorConfiguration.Anchors.FirstOrDefault(a => a.Key == "maghrib")?.Id == x.AnchorId);
            if (existing?.StartTime != time || existing?.IsCancelled == true)
                BulkPreview.Add(new(date, time, existing?.DurationMinutes ?? AlQalamExpansionRules.PrayerMinutes));
        }
        BulkMessage = $"{BulkPreview.Count} date(s) will change.";
    }

    [RelayCommand] private void AddStandingTime()
    {
        OrganizationAnchor? anchor = _anchorConfiguration is { Anchors.Count: > 0 } configuration ? configuration.Anchors[0] : null;
        if (anchor is not null) StandingTimes.Add(new() { IsNew = true, AnchorId = anchor.Id });
    }
    [RelayCommand]
    private async Task SaveStandingTimeAsync(AnchorStandingEditorItem item, CancellationToken token)
    {
        try
        {
            Guid org = await _gateway.GetCurrentOrganizationIdAsync(token);
            await _gateway.SaveAnchorStandingTimeAsync(new(item.Id, org, item.AnchorId, item.Weekday,
                TimeOnly.FromTimeSpan(item.Start), item.DurationMinutes, DateOnly.FromDateTime(item.EffectiveFrom)), item.IsNew, token);
            _anchorConfiguration = await _gateway.GetAnchorConfigurationAsync(token); item.Error = null;
        }
        catch (ServerWriteException ex) { item.Error = ex.Message; }
    }
    [RelayCommand] private void AddAnchorOverride()
    {
        OrganizationAnchor? anchor = _anchorConfiguration is { Anchors.Count: > 0 } configuration ? configuration.Anchors[0] : null;
        if (anchor is not null) AnchorDateOverrides.Add(new() { IsNew = true, AnchorId = anchor.Id });
    }
    [RelayCommand]
    private async Task SaveAnchorOverrideAsync(AnchorOverrideEditorItem item, CancellationToken token)
    {
        try
        {
            if (!item.IsCancelled && item.Start is null) { item.Error = "A time is required unless the anchor is cancelled."; return; }
            Guid org = await _gateway.GetCurrentOrganizationIdAsync(token);
            await _gateway.SaveAnchorDateOverrideAsync(new(item.Id, org, item.AnchorId, DateOnly.FromDateTime(item.Date),
                item.IsCancelled ? null : TimeOnly.FromTimeSpan(item.Start!.Value), item.IsCancelled ? null : item.DurationMinutes, item.IsCancelled), item.IsNew, token);
            _anchorConfiguration = await _gateway.GetAnchorConfigurationAsync(token); item.Error = null;
        }
        catch (ServerWriteException ex) { item.Error = ex.Message; }
    }

    [RelayCommand]
    private async Task ApplyMaghribBulkPasteAsync(CancellationToken token)
    {
        PreviewMaghribBulkPaste();
        if (!string.IsNullOrEmpty(BulkMessage) && !BulkMessage.EndsWith("will change.", StringComparison.Ordinal)) return;
        OrganizationAnchor? maghrib = _anchorConfiguration?.Anchors.FirstOrDefault(x => x.Key == "maghrib");
        if (maghrib is null) { BulkMessage = "The Maghrib anchor is unavailable."; return; }
        try
        {
            int changed = await _gateway.BulkUpsertAnchorDateOverridesAsync(maghrib.Id, BulkPreview.ToArray(), token);
            _anchorConfiguration = await _gateway.GetAnchorConfigurationAsync(token);
            BulkMessage = $"Saved {changed} Maghrib date(s) atomically.";
        }
        catch (ServerWriteException ex) { BulkMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task RegenerateNowAsync(CancellationToken token)
    {
        try { GeneratorMaintenanceRun run = await _gateway.RegenerateGeneratedTimetablesAsync(token); LastGeneratorRunAt = run.StartedAt; OnPropertyChanged(nameof(IsMaintenanceOverdue)); MaintenanceMessage = run.Error ?? $"Regenerated {run.TimetablesWritten} timetable(s)."; await _sync.SyncTableAsync(CacheTable.Periods, token); await RefreshClashWarningsAsync(token); }
        catch (ServerWriteException ex) { MaintenanceMessage = ex.Message; }
    }

    private async Task RefreshClashWarningsAsync(CancellationToken token)
    {
        try
        {
            IReadOnlyList<Timetable> timetables = await _repository.GetAllAsync(token);
            Dictionary<Guid, Timetable> byId = timetables.ToDictionary(item => item.Id);
            WeekSchedule schedule = await _week.GetAsync(token);
            WeekScheduleEntry[] classRows = schedule.AllEntries.Where(entry =>
                entry.AudienceClassId is not null && entry.TimetableId is not null).ToArray();
            if (classRows.GroupBy(entry => (entry.Weekday, entry.AudienceClassId)).Any(group => group.Count() > 1))
            { ClashWarningMessage = "Cross-class clash check unavailable: the week schedule has duplicate class rows."; return; }

            Guid[] referenced = classRows.Select(entry => entry.TimetableId!.Value).Distinct().ToArray();
            var generated = new HashSet<Guid>();
            foreach (Guid timetableId in referenced)
                if ((await _gateway.GetGeneratorAuthoringAsync(timetableId, token)).Definition is not null)
                    generated.Add(timetableId);
            Dictionary<Guid, string> classNames = _classes is null ? [] :
                (await _classes.GetAllAsync(token)).ToDictionary(item => item.Id, item => item.Name);
            var warnings = new List<string>();
            foreach (IGrouping<DayOfWeek, WeekScheduleEntry> day in classRows.GroupBy(entry => entry.Weekday))
            {
                WeekScheduleEntry[] candidates = day.Where(entry => generated.Contains(entry.TimetableId!.Value)).ToArray();
                for (int leftIndex = 0; leftIndex < candidates.Length; leftIndex++)
                for (int rightIndex = leftIndex + 1; rightIndex < candidates.Length; rightIndex++)
                {
                    WeekScheduleEntry left = candidates[leftIndex], right = candidates[rightIndex];
                    if (left.AudienceClassId == right.AudienceClassId || left.TimetableId == right.TimetableId) continue;
                    if (!byId.TryGetValue(left.TimetableId!.Value, out Timetable? leftTimetable) ||
                        !byId.TryGetValue(right.TimetableId!.Value, out Timetable? rightTimetable))
                    { ClashWarningMessage = "Cross-class clash check unavailable: a scheduled timetable is missing locally."; return; }
                    IReadOnlyList<GeneratedPeriodClash> clashes = GeneratedTimetableClashDetector.Find(
                        leftTimetable.Periods, rightTimetable.Periods);
                    GeneratedPeriodClash? clash = clashes.Count == 0 ? null : clashes[0];
                    if (clash is null) continue;
                    string leftClass = classNames.GetValueOrDefault(left.AudienceClassId!.Value, left.AudienceClassId.Value.ToString("D"));
                    string rightClass = classNames.GetValueOrDefault(right.AudienceClassId!.Value, right.AudienceClassId.Value.ToString("D"));
                    warnings.Add($"{day.Key}: {leftClass} '{clash.Left.Name}' and {rightClass} '{clash.Right.Name}' disagree from {clash.Start:HH:mm} to {clash.End:HH:mm}.");
                }
            }
            ClashWarningMessage = warnings.Count == 0 ? null : "Cross-class clock clash: " + string.Join(" ", warnings);
        }
        catch (NotSupportedException) { ClashWarningMessage = null; }
    }

    public async Task RegenerateOnAdminEntryAsync(CancellationToken token = default)
    {
        try { await RegenerateNowAsync(token); }
        catch (NotSupportedException) { }
    }
    public void Receive(DataChanged message)
    {
        if (message.Table is not (CacheTable.Timetables or CacheTable.Periods) || _ownWriteDepth > 0) return;
        void ApplyChange()
        {
            if (IsDirty) HasConflict = true;
            else _ = LoadAsync();
        }

        UiDispatch.Run(ApplyChange);
    }
}

public sealed record AudienceOption(Guid? Id, string Name);
public partial class WeekScheduleItem : ObservableObject
{
    public Guid Id { get; init; }
    public int Weekday { get; init; }
    public string DayLabel { get; init; } = string.Empty;
    public bool IsNew { get; init; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefault))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private Guid? _audienceClassId;
    [ObservableProperty] private Guid? _timetableId;
    [ObservableProperty] private string? _error;
    public bool IsDefault => AudienceClassId is null && !IsNew;
    public bool CanDelete => IsNew || AudienceClassId is not null;
}
public partial class WeekScheduleViewModel(IWeekScheduleRepository repository, ITimetableRepository timetables, ISupabaseGateway gateway, ISyncService sync, IWindowService windows, IClassRepository? classes = null) : ObservableObject
{
    public ObservableCollection<WeekScheduleItem> Rows { get; } = []; public ObservableCollection<Timetable> Timetables { get; } = []; public ObservableCollection<AudienceOption> Audiences { get; } = [];
    public async Task LoadAsync(CancellationToken token = default)
    {
        WeekSchedule schedule = await repository.GetAsync(token); var classItems = classes is null ? [] : await classes.GetAllAsync(token);
        Audiences.Clear(); Audiences.Add(new(null, "Default (everyone)")); foreach (var item in classItems.OrderBy(x => x.SortOrder).ThenBy(x => x.Name)) Audiences.Add(new(item.Id, item.Name));
        Rows.Clear();
        for (int weekday = 0; weekday < 7; weekday++)
        {
            DayOfWeek day = (DayOfWeek)((weekday + 1) % 7); bool first = true;
            IReadOnlyList<WeekScheduleEntry> dayEntries = schedule.EntriesFor(day);
            if (dayEntries.Count == 0) dayEntries = [new WeekScheduleEntry(Guid.Empty, day, null, null)];
            foreach (WeekScheduleEntry entry in dayEntries.OrderBy(x => x.AudienceClassId is null ? 0 : 1).ThenBy(x => Audiences.FirstOrDefault(a => a.Id == x.AudienceClassId)?.Name))
            { Rows.Add(new() { Id = entry.Id, Weekday = weekday, DayLabel = first ? day.ToString() : string.Empty, AudienceClassId = entry.AudienceClassId, TimetableId = entry.TimetableId }); first = false; }
        }
        Timetables.Clear(); foreach (Timetable t in (await timetables.GetAllAsync(token)).Where(x => !x.IsArchived)) Timetables.Add(t);
    }
    [RelayCommand] private void AddRow(WeekScheduleItem row) => Rows.Insert(Rows.IndexOf(row) + 1, new() { Id = Guid.NewGuid(), Weekday = row.Weekday, IsNew = true });
    [RelayCommand]
    private async Task DeleteRowAsync(WeekScheduleItem row, CancellationToken token)
    {
        if (row.IsNew) { Rows.Remove(row); return; }
        if (row.AudienceClassId is not { } classId || !windows.Confirm("Delete this class-specific week schedule row?", "Delete row")) return;
        try { await gateway.DeleteWeekScheduleRowAsync(row.Weekday, classId, token); await sync.SyncTableAsync(CacheTable.WeekSchedule, token); await LoadAsync(token); }
        catch (ServerDeniedException) { row.Error = "Your role changed."; windows.CloseAdminWindow(); }
        catch (ServerWriteException ex) { row.Error = ex.Message; }
    }
    [RelayCommand] private async Task SaveRowAsync(WeekScheduleItem row, CancellationToken token) { if (Rows.Any(x => x != row && x.Weekday == row.Weekday && x.AudienceClassId == row.AudienceClassId)) { row.Error = $"That class already has a row for {((DayOfWeek)((row.Weekday + 1) % 7))}."; return; } try { await gateway.SaveWeekScheduleRowAsync(row.Weekday, row.AudienceClassId, row.TimetableId, token); await sync.SyncTableAsync(CacheTable.WeekSchedule, token); row.Error = null; if (row.IsNew) await LoadAsync(token); } catch (ServerDeniedException) { row.Error = "Your role changed."; windows.CloseAdminWindow(); } catch (DuplicateRowException) { row.Error = $"That class already has a row for {((DayOfWeek)((row.Weekday + 1) % 7))}."; } catch (ServerWriteException ex) { row.Error = ex.Message; } }
    [RelayCommand] private static void SetNoSchool(WeekScheduleItem row) => row.TimetableId = null;
}

public partial class OverrideEditorItem : ObservableObject { public Guid Id { get; init; } [ObservableProperty] private DateTime _date = DateTime.Today; [ObservableProperty] private Guid? _timetableId; [ObservableProperty] private string? _note; }
public partial class OverridesViewModel(IDateOverrideRepository repository, ITimetableRepository timetables, ISupabaseGateway gateway, ISyncService sync, IWindowService windows) : ObservableObject
{
    private DateOnly? _confirmedReplaceDate;
    public ObservableCollection<OverrideEditorItem> Items { get; } = []; public ObservableCollection<Timetable> Timetables { get; } = []; [ObservableProperty] private bool _showPast; [ObservableProperty] private string? _error;
    public async Task LoadAsync(CancellationToken token = default) { Items.Clear(); foreach (DateOverride x in (await repository.GetAllAsync(token)).Where(x => ShowPast || x.Date >= DateOnly.FromDateTime(DateTime.Today)).OrderBy(x => x.Date)) Items.Add(new() { Id = x.Id, Date = x.Date.ToDateTime(TimeOnly.MinValue), TimetableId = x.TimetableId, Note = x.Note }); Timetables.Clear(); foreach (Timetable t in (await timetables.GetAllAsync(token)).Where(x => !x.IsArchived)) Timetables.Add(t); }
    [RelayCommand] private void Add() => Items.Add(new() { Id = Guid.NewGuid() });
    [RelayCommand] private static void SetClosed(OverrideEditorItem item) => item.TimetableId = null;
    [RelayCommand] private async Task SaveAsync(OverrideEditorItem item, CancellationToken token) { try { Guid org = await gateway.GetCurrentOrganizationIdAsync(token); DateOnly date = DateOnly.FromDateTime(item.Date); DateOverride? existing = (await repository.GetAllAsync(token)).FirstOrDefault(x => x.Date == date); if (existing is not null && existing.Id != item.Id && _confirmedReplaceDate != date) { _confirmedReplaceDate = date; Error = "An override already exists for this date. Click Save again to confirm replacement."; return; } Guid id = existing?.Id ?? item.Id; var row = new DateOverrideRow(id, org, date, item.TimetableId, item.Note); if (existing is null) await gateway.InsertAsync(CacheTable.DateOverrides, row, token); else await gateway.UpdateAsync(CacheTable.DateOverrides, id, row, token); _confirmedReplaceDate = null; Error = null; await sync.SyncTableAsync(CacheTable.DateOverrides, token); await LoadAsync(token); } catch (ServerDeniedException) { Error = "Your role changed."; windows.CloseAdminWindow(); } catch (DuplicateRowException) { Error = "An override already exists for this date. Reload and replace it."; } }
    [RelayCommand] private async Task DeleteAsync(OverrideEditorItem item, CancellationToken token) { if (!windows.Confirm($"Delete the override for {item.Date:d}?", "Delete override")) return; await gateway.DeleteAsync(CacheTable.DateOverrides, item.Id, token); await sync.SyncTableAsync(CacheTable.DateOverrides, token); await LoadAsync(token); }
}

public enum ExpiryPreset { EndOfDay, EndOfWeek, Custom, Never }

public partial class ClassEditorItem : ObservableObject
{
    public Guid Id { get; init; }
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _sortOrder;
}

public partial class ClassesViewModel(IClassRepository repository, ISupabaseGateway gateway, ISyncService sync) : ObservableObject
{
    public ObservableCollection<ClassEditorItem> Items { get; } = [];
    [ObservableProperty] private string? _error;

    public async Task LoadAsync(CancellationToken token = default)
    {
        IReadOnlyList<AqiClock.Domain.Entities.Class> classes = await repository.GetAllAsync(token);
        Items.Clear(); foreach (var item in classes) Items.Add(new() { Id = item.Id, Name = item.Name, SortOrder = item.SortOrder });
    }

    [RelayCommand] private void Add() => Items.Add(new() { Id = Guid.NewGuid(), Name = "New class", SortOrder = Items.Count == 0 ? 0 : Items.Max(x => x.SortOrder) + 1 });
    [RelayCommand] private async Task SaveAsync(ClassEditorItem item, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) { Error = "Class name is required."; return; }
        try
        {
            Guid org = await gateway.GetCurrentOrganizationIdAsync(token);
            var row = new ClassRow(item.Id, org, item.Name.Trim(), item.SortOrder);
            if ((await repository.GetAllAsync(token)).Any(x => x.Id == item.Id)) await gateway.UpdateAsync(CacheTable.Classes, item.Id, row, token);
            else await gateway.InsertAsync(CacheTable.Classes, row, token);
            await sync.SyncTableAsync(CacheTable.Classes, token); await LoadAsync(token); Error = null;
        }
        catch (DuplicateRowException) { Error = "A class already uses that name or sort order."; }
    }
    [RelayCommand] private async Task DeleteAsync(ClassEditorItem item, CancellationToken token)
    {
        try { await gateway.DeleteAsync(CacheTable.Classes, item.Id, token); await sync.SyncTableAsync(CacheTable.Classes, token); await LoadAsync(token); Error = null; }
        catch (ReferencedRowException) { Error = "This class is referenced by an announcement or the week schedule. Reassign or delete that reference first."; }
    }
}

public partial class AnnouncementComposeViewModel(ISupabaseGateway gateway, ISyncService sync, ISessionService session, IAnnouncementRepository repository, IClassRepository classRepository, IWindowService windows) : ObservableObject
{
    public AnnouncementComposeViewModel(ISupabaseGateway gateway, ISyncService sync, ISessionService session, IAnnouncementRepository repository, IWindowService windows)
        : this(gateway, sync, session, repository, new EmptyClassRepository(), windows) { }
    [ObservableProperty] private string _title = string.Empty; [ObservableProperty] private string _body = string.Empty; [ObservableProperty] private ExpiryPreset _expiry = ExpiryPreset.EndOfDay; [ObservableProperty] private DateTime? _customExpiry; [ObservableProperty] private AudienceType _audience = AudienceType.Everyone; [ObservableProperty] private Guid? _audienceClassId; [ObservableProperty] private UpdateType _updateType = UpdateType.General; [ObservableProperty] private DateTime? _publishAt; [ObservableProperty] private string? _eMasjidLink; [ObservableProperty] private string? _error;
    public ObservableCollection<Announcement> Items { get; } = []; public ObservableCollection<Announcement> History { get; } = []; public ObservableCollection<AqiClock.Domain.Entities.Class> Classes { get; } = [];
    [ObservableProperty] private string _publishTime = "09:00";
    public IReadOnlyList<ExpiryPreset> Presets { get; } = Enum.GetValues<ExpiryPreset>(); public IReadOnlyList<AudienceType> Audiences { get; } = Enum.GetValues<AudienceType>().Where(x => x != AudienceType.Graduates).ToArray(); public IReadOnlyList<UpdateType> UpdateTypes { get; } = Enum.GetValues<UpdateType>();
    public async Task LoadAsync(CancellationToken token = default) { Items.Clear(); foreach (Announcement x in await repository.GetCurrentAsync(DateTimeOffset.Now, token)) Items.Add(x); History.Clear(); foreach (Announcement x in await repository.GetHistoryAsync(token)) History.Add(x); Classes.Clear(); foreach (AqiClock.Domain.Entities.Class x in await classRepository.GetAllAsync(token)) Classes.Add(x); }
    [RelayCommand] private async Task PublishAsync(CancellationToken token) { if (Title.Length is 0 or > 200 || Body.Length is 0 or > 2000) { Error = "Title and body are required and must fit the limits."; return; } if (Audience == AudienceType.SpecificClass && AudienceClassId is null) { Error = "Choose a class for this audience."; return; } if (!string.IsNullOrWhiteSpace(EMasjidLink) && (!Uri.TryCreate(EMasjidLink, UriKind.Absolute, out Uri? link) || link.Scheme != Uri.UriSchemeHttps)) { Error = "The e-Masjid link must be a valid HTTPS URL."; return; } TimeOnly publishTime = default; if (PublishAt is not null && !TimeOnly.TryParse(PublishTime, CultureInfo.CurrentCulture, DateTimeStyles.None, out publishTime)) { Error = "Enter the publish time as HH:mm."; return; } try { Guid org = await gateway.GetCurrentOrganizationIdAsync(token); Guid actor = session.Current.UserId ?? throw new InvalidOperationException("Sign in required."); DateTimeOffset now = DateTimeOffset.Now; DateTimeOffset? publish = PublishAt is null ? null : new DateTimeOffset(DateOnly.FromDateTime(PublishAt.Value).ToDateTime(publishTime)); string status = publish > now ? "scheduled" : "published"; DateTimeOffset? expires = ResolveExpiry(Expiry, CustomExpiry, publish ?? now); if (publish is not null && expires <= publish) { Error = "Expiry must be later than the publication time."; return; } await gateway.InsertAsync(CacheTable.Announcements, new AnnouncementRow(Guid.NewGuid(), org, Title.Trim(), Body.Trim(), expires, actor, now, Snake(Audience), Audience == AudienceType.SpecificClass ? AudienceClassId : null, Snake(UpdateType), publish, string.IsNullOrWhiteSpace(EMasjidLink) ? null : EMasjidLink.Trim(), status), token); await sync.SyncTableAsync(CacheTable.Announcements, token); Title = Body = string.Empty; PublishAt = null; EMasjidLink = null; Error = null; await LoadAsync(token); } catch (ServerDeniedException) { Error = "Your role changed."; windows.CloseAdminWindow(); } }
    [RelayCommand] private async Task DeleteAsync(Announcement item, CancellationToken token) { if (!windows.Confirm($"Move the announcement '{item.Title}' to history?", "Delete announcement")) return; await UpdateStateAsync(item, item.Status, item.PublishAt, DateTimeOffset.Now, token); }
    [RelayCommand] private async Task PublishItemAsync(Announcement item, CancellationToken token)
    {
        if (item.DeletedAt is not null) { Error = "Deleted announcements cannot be republished."; return; }
        await UpdateStateAsync(item, AnnouncementStatus.Published, DateTimeOffset.Now, item.DeletedAt, token);
    }
    private async Task UpdateStateAsync(Announcement item, AnnouncementStatus status, DateTimeOffset? publishAt, DateTimeOffset? deletedAt, CancellationToken token) { Guid org = await gateway.GetCurrentOrganizationIdAsync(token); await gateway.UpdateAsync(CacheTable.Announcements, item.Id, new AnnouncementRow(item.Id, org, item.Title, item.Body, item.ExpiresAt, item.CreatedBy, item.CreatedAt, Snake(item.AudienceType), item.AudienceClassId, Snake(item.UpdateType), publishAt, item.EMasjidLink, Snake(status), deletedAt), token); await sync.SyncTableAsync(CacheTable.Announcements, token); await LoadAsync(token); }
    private static string Snake<T>(T value) where T : struct, Enum => string.Concat(value.ToString().Select((character, index) => char.IsUpper(character) && index > 0 ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
    public static DateTimeOffset? ResolveExpiry(ExpiryPreset preset, DateTime? custom, DateTimeOffset now) => preset switch { ExpiryPreset.EndOfDay => new DateTimeOffset(now.Date.AddDays(1).AddTicks(-1), now.Offset), ExpiryPreset.EndOfWeek => new DateTimeOffset(now.Date.AddDays(((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7 + 1).AddTicks(-1), now.Offset), ExpiryPreset.Custom => custom is null ? null : new DateTimeOffset(custom.Value), _ => null };
}

public sealed record AuditDisplay(string When, string Who, string Action, string What);
public partial class AuditViewModel(ISupabaseGateway gateway, IProfileRepository profiles, ISyncService sync) : ObservableObject
{
    [ObservableProperty] private string? _message; public ObservableCollection<AuditDisplay> Items { get; } = [];
    [RelayCommand]
    public async Task LoadAsync(CancellationToken token = default) { Items.Clear(); if (sync.State != ConnectivityState.Online) { Message = "Connect to view history"; return; } Dictionary<Guid, string> names = (await profiles.GetAllAsync(token)).ToDictionary(x => x.Id, x => x.DisplayName); foreach (AuditEntry x in await gateway.GetAuditEntriesAsync(100, token)) Items.Add(new(x.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture), x.ActorId is { } id ? names.GetValueOrDefault(id, "system") : "system", x.Action, Humanize(x))); Message = null; }
    private static string Humanize(AuditEntry entry)
    {
        JsonObject? image = entry.After ?? entry.Before;
        string name = image?["name"]?.GetValue<string>() ?? image?["title"]?.GetValue<string>() ?? image?["display_name"]?.GetValue<string>() ?? image?["date"]?.GetValue<string>() ?? entry.EntityId.ToString();
        if (entry.EntityType == "week_schedule" && image?["weekday"]?.GetValue<int>() is int weekday)
            name = ((DayOfWeek)((weekday + 1) % 7)).ToString();
        return $"{entry.EntityType.Replace('_', ' ')} '{name}'";
    }
}

public partial class UserEditorItem : ObservableObject { public Guid Id { get; init; } public string DisplayName { get; init; } = string.Empty; public UserRole OriginalRole { get; set; } public bool OriginalIsActive { get; set; } public bool IsEditable { get; init; } = true; [ObservableProperty] private UserRole _role; [ObservableProperty] private bool _isActive; [ObservableProperty] private string? _error; public string Email { get; init; } = "Not available"; }
public partial class UsersViewModel(IProfileRepository profiles, ISupabaseGateway gateway, ISyncService sync, ISessionService session, IWindowService windows) : ObservableObject
{
    public ObservableCollection<UserEditorItem> Items { get; } = [];
    public IReadOnlyList<UserRole> Roles { get; } = [UserRole.Teacher, UserRole.Admin];
    public async Task LoadAsync(CancellationToken token = default) { Items.Clear(); foreach (Profile p in (await profiles.GetAllAsync(token)).OrderByDescending(x => x.Id == session.Current.UserId).ThenByDescending(x => x.Role == UserRole.Admin).ThenBy(x => x.DisplayName)) Items.Add(new() { Id = p.Id, DisplayName = p.DisplayName, Role = p.Role, OriginalRole = p.Role, IsActive = p.IsActive, OriginalIsActive = p.IsActive, Email = p.Id == session.Current.UserId ? session.Current.Email ?? "Not available" : "Not stored (MVP)" }); Items.Add(new() { DisplayName = "Graduate profiles — coming soon", Role = UserRole.Graduate, OriginalRole = UserRole.Graduate, IsActive = false, IsEditable = false, Error = "Reserved for a future release." }); }
    [RelayCommand] private async Task SaveAsync(UserEditorItem item, CancellationToken token) { if (!item.IsEditable) return; bool changed = item.Role != item.OriginalRole || item.IsActive != item.OriginalIsActive; if (!changed) return; if (!windows.Confirm($"Apply the role and account-status changes for {item.DisplayName}?", "Update user")) return; try { await gateway.UpdateProfileAsync(item.Id, item.Role == UserRole.Admin ? "admin" : "teacher", item.IsActive, token); await sync.SyncTableAsync(CacheTable.Profiles, token); item.OriginalRole = item.Role; item.OriginalIsActive = item.IsActive; item.Error = null; } catch (LastAdminException) { item.Error = "You cannot demote or deactivate the last active admin. Promote someone else first."; } catch (ServerDeniedException) { item.Error = "Your role changed."; windows.CloseAdminWindow("Your role changed. The admin editor has been closed."); } }
}
