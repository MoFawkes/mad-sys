using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Imaging;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.App.Services;
using AqiClock.Domain.Entities;
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
    public ObservableCollection<Timetable> Items { get; } = [];
    public ObservableCollection<PeriodEditorItem> Periods { get; } = [];

    public TimetableEditorViewModel(ISupabaseGateway gateway, ISyncService sync, ITimetableRepository repository, IWeekScheduleRepository week, IDateOverrideRepository overrides, IWindowService windows, IMessenger messenger)
    { _gateway = gateway; _sync = sync; _repository = repository; _week = week; _overrides = overrides; _windows = windows; Periods.CollectionChanged += OnPeriodsChanged; messenger.Register(this); }

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
        if (target is not null) Select(target);
    }

    partial void OnSelectedChanged(Timetable? value) { if (value is not null) Select(value); }
    private void Select(Timetable value) { _loading = true; Name = value.Name; IsArchived = value.IsArchived; DetachPeriodHandlers(); Periods.Clear(); foreach (Period p in value.Periods.OrderBy(x => x.SortOrder)) Periods.Add(new() { Id = p.Id, Name = p.Name, Start = p.StartTime.ToTimeSpan(), End = p.EndTime.ToTimeSpan(), IsLesson = p.IsLesson, SortOrder = p.SortOrder }); IsDirty = false; HasConflict = false; ValidationMessage = null; _loading = false; }

    /// <summary>Clear() raises a Reset with no OldItems, so discarded rows must be detached here or they keep marking the editor dirty.</summary>
    private void DetachPeriodHandlers() { foreach (PeriodEditorItem item in Periods) item.PropertyChanged -= OnPeriodChanged; }
    partial void OnNameChanged(string value) { if (!_loading) IsDirty = true; }
    partial void OnIsArchivedChanged(bool value) { if (!_loading) IsDirty = true; }
    private void OnPeriodsChanged(object? sender, NotifyCollectionChangedEventArgs args) { if (args.OldItems is not null) foreach (PeriodEditorItem item in args.OldItems) item.PropertyChanged -= OnPeriodChanged; if (args.NewItems is not null) foreach (PeriodEditorItem item in args.NewItems) item.PropertyChanged += OnPeriodChanged; if (!_loading) IsDirty = true; }
    private void OnPeriodChanged(object? sender, PropertyChangedEventArgs args) { if (!_loading) IsDirty = true; }

    [RelayCommand] private void NewTimetable() { Selected = new Timetable(Guid.NewGuid(), "New timetable", false, []); IsDirty = true; }
    [RelayCommand] private void AddPeriod() { Periods.Add(new() { Id = Guid.NewGuid(), Name = "New period", Start = new(9, 0, 0), End = new(10, 0, 0), SortOrder = Periods.Count }); IsDirty = true; }
    [RelayCommand] private void RemovePeriod(PeriodEditorItem item) { Periods.Remove(item); IsDirty = true; }
    [RelayCommand] private void MoveUp(PeriodEditorItem item) { int index = Periods.IndexOf(item); if (index > 0) { Periods.Move(index, index - 1); IsDirty = true; } }
    [RelayCommand] private void MoveDown(PeriodEditorItem item) { int index = Periods.IndexOf(item); if (index >= 0 && index < Periods.Count - 1) { Periods.Move(index, index + 1); IsDirty = true; } }
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
