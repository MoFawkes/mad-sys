using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.App.ViewModels;

public partial class AdminViewModel : ObservableObject, IRecipient<SessionChanged>, IRecipient<ConnectivityChanged>
{
    private readonly IWindowService _windows;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string? _banner;
    public TimetableEditorViewModel Timetables { get; }
    public WeekScheduleViewModel WeekSchedule { get; }
    public OverridesViewModel Overrides { get; }
    public AnnouncementComposeViewModel Announcements { get; }
    public AuditViewModel Audit { get; }
    public UsersViewModel Users { get; }

    public AdminViewModel(TimetableEditorViewModel timetables, WeekScheduleViewModel weekSchedule, OverridesViewModel overrides, AnnouncementComposeViewModel announcements, AuditViewModel audit, UsersViewModel users, ISyncService sync, IWindowService windows, IMessenger messenger)
    {
        Timetables = timetables; WeekSchedule = weekSchedule; Overrides = overrides; Announcements = announcements; Audit = audit; Users = users; _windows = windows; IsOnline = sync.State == ConnectivityState.Online;
        messenger.Register<SessionChanged>(this); messenger.Register<ConnectivityChanged>(this);
    }

    public async Task InitializeAsync(CancellationToken token = default) => await Task.WhenAll(Timetables.LoadAsync(token), WeekSchedule.LoadAsync(token), Overrides.LoadAsync(token), Announcements.LoadAsync(token), Audit.LoadAsync(token), Users.LoadAsync(token));
    public void Receive(SessionChanged message) => RunOnUiThread(() =>
    {
        if (message.State.Role != UserRole.Admin)
        {
            const string reason = "Your role changed. The admin editor has been closed.";
            Banner = reason;
        }
    });

    public void Receive(ConnectivityChanged message) => RunOnUiThread(() =>
    {
        IsOnline = message.State == ConnectivityState.Online;
        Banner = IsOnline ? null : "Editing is unavailable while offline.";
    });

    private static void RunOnUiThread(Action action)
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || !dispatcher.Thread.IsAlive || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess()) action();
        else _ = dispatcher.BeginInvoke(action);
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
    private readonly ISupabaseGateway _gateway; private readonly ISyncService _sync; private readonly ITimetableRepository _repository; private readonly IWeekScheduleRepository _week; private readonly IDateOverrideRepository _overrides; private readonly IWindowService _windows;
    private HashSet<Guid> _originalPeriodIds = [];
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
    private void Select(Timetable value) { _loading = true; Name = value.Name; IsArchived = value.IsArchived; Periods.Clear(); foreach (Period p in value.Periods.OrderBy(x => x.SortOrder)) Periods.Add(new() { Id = p.Id, Name = p.Name, Start = p.StartTime.ToTimeSpan(), End = p.EndTime.ToTimeSpan(), IsLesson = p.IsLesson, SortOrder = p.SortOrder }); _originalPeriodIds = value.Periods.Select(x => x.Id).ToHashSet(); IsDirty = false; HasConflict = false; ValidationMessage = null; _loading = false; }
    partial void OnNameChanged(string value) { if (!_loading) IsDirty = true; }
    partial void OnIsArchivedChanged(bool value) { if (!_loading) IsDirty = true; }
    private void OnPeriodsChanged(object? sender, NotifyCollectionChangedEventArgs args) { if (args.NewItems is not null) foreach (PeriodEditorItem item in args.NewItems) item.PropertyChanged += OnPeriodChanged; if (!_loading) IsDirty = true; }
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
            Guid org = await _gateway.GetCurrentOrganizationIdAsync(token); bool exists = Items.Any(x => x.Id == Selected.Id);
            var row = new TimetableRow(Selected.Id, org, Name.Trim(), IsArchived);
            if (exists) await _gateway.UpdateAsync(CacheTable.Timetables, Selected.Id, row, token); else await _gateway.InsertAsync(CacheTable.Timetables, row, token);
            for (int index = 0; index < Periods.Count; index++)
            {
                PeriodEditorItem p = Periods[index]; var period = new PeriodRow(p.Id, Selected.Id, p.Name.Trim(), TimeOnly.FromTimeSpan(p.Start), TimeOnly.FromTimeSpan(p.End), index, p.IsLesson);
                if (_originalPeriodIds.Contains(p.Id)) await _gateway.UpdateAsync(CacheTable.Periods, p.Id, period, token); else await _gateway.InsertAsync(CacheTable.Periods, period, token);
            }
            foreach (Guid deleted in _originalPeriodIds.Except(Periods.Select(x => x.Id))) await _gateway.DeleteAsync(CacheTable.Periods, deleted, token);
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
        WeekSchedule week = await _week.GetAsync(token); foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) if (week.TimetableIdFor(day) == Selected.Id) used.Add(day.ToString());
        foreach (DateOverride item in await _overrides.GetAllAsync(token)) if (item.TimetableId == Selected.Id) used.Add(item.Date.ToString("d MMM", CultureInfo.CurrentCulture));
        if (used.Count > 0) { ValidationMessage = $"Used by: {string.Join(", ", used)} — reassign first"; return; }
        if (!_windows.Confirm($"Delete '{Selected.Name}' and all of its periods? This cannot be undone.", "Delete timetable")) return;
        try { await _gateway.DeleteAsync(CacheTable.Timetables, Selected.Id, token); await _sync.SyncTableAsync(CacheTable.Timetables, token); await _sync.SyncTableAsync(CacheTable.Periods, token); Selected = null; await LoadAsync(token); }
        catch (ReferencedRowException) { ValidationMessage = "This timetable became referenced remotely — reassign it first."; }
        catch (ServerDeniedException) { ValidationMessage = "Your role changed."; _windows.CloseAdminWindow(); }
    }

    [RelayCommand] private async Task DuplicateAsync(CancellationToken token) { if (Selected is null) return; Timetable source = Selected; NewTimetable(); Name = source.Name + " copy"; Periods.Clear(); foreach (Period p in source.Periods) Periods.Add(new() { Id = Guid.NewGuid(), Name = p.Name, Start = p.StartTime.ToTimeSpan(), End = p.EndTime.ToTimeSpan(), IsLesson = p.IsLesson, SortOrder = p.SortOrder }); await SaveAsync(token); }
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

        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || !dispatcher.Thread.IsAlive || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess()) ApplyChange();
        else _ = dispatcher.BeginInvoke(ApplyChange);
    }
}

public partial class WeekScheduleItem : ObservableObject { public Guid Id { get; init; } public int Weekday { get; init; } public string Day => ((DayOfWeek)((Weekday + 1) % 7)).ToString(); [ObservableProperty] private Guid? _timetableId; [ObservableProperty] private string? _error; }
public partial class WeekScheduleViewModel(IWeekScheduleRepository repository, ITimetableRepository timetables, ISupabaseGateway gateway, ISyncService sync, IWindowService windows) : ObservableObject
{
    public ObservableCollection<WeekScheduleItem> Rows { get; } = []; public ObservableCollection<Timetable> Timetables { get; } = [];
    public async Task LoadAsync(CancellationToken token = default) { WeekSchedule schedule = await repository.GetAsync(token); Rows.Clear(); for (int weekday = 0; weekday < 7; weekday++) { DayOfWeek day = (DayOfWeek)((weekday + 1) % 7); Rows.Add(new() { Weekday = weekday, TimetableId = schedule.TimetableIdFor(day) }); } Timetables.Clear(); foreach (Timetable t in (await timetables.GetAllAsync(token)).Where(x => !x.IsArchived)) Timetables.Add(t); }
    [RelayCommand] private async Task SaveRowAsync(WeekScheduleItem row, CancellationToken token) { try { await gateway.UpdateWeekScheduleAsync(row.Weekday, row.TimetableId, token); await sync.SyncTableAsync(CacheTable.WeekSchedule, token); row.Error = null; } catch (ServerDeniedException) { row.Error = "Your role changed."; windows.CloseAdminWindow(); } catch (ServerWriteException ex) { row.Error = ex.Message; } }
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
public partial class AnnouncementComposeViewModel(ISupabaseGateway gateway, ISyncService sync, ISessionService session, IAnnouncementRepository repository, IWindowService windows) : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty; [ObservableProperty] private string _body = string.Empty; [ObservableProperty] private ExpiryPreset _expiry = ExpiryPreset.EndOfDay; [ObservableProperty] private DateTime? _customExpiry; [ObservableProperty] private string? _error;
    public ObservableCollection<Announcement> Items { get; } = []; public IReadOnlyList<ExpiryPreset> Presets { get; } = Enum.GetValues<ExpiryPreset>();
    public async Task LoadAsync(CancellationToken token = default) { Items.Clear(); foreach (Announcement x in await repository.GetCurrentAsync(DateTimeOffset.Now, token)) Items.Add(x); }
    [RelayCommand] private async Task PublishAsync(CancellationToken token) { if (Title.Length is 0 or > 200 || Body.Length is 0 or > 2000) { Error = "Title and body are required and must fit the limits."; return; } try { Guid org = await gateway.GetCurrentOrganizationIdAsync(token); Guid actor = session.Current.UserId ?? throw new InvalidOperationException("Sign in required."); DateTimeOffset? expires = ResolveExpiry(Expiry, CustomExpiry, DateTimeOffset.Now); await gateway.InsertAsync(CacheTable.Announcements, new AnnouncementRow(Guid.NewGuid(), org, Title.Trim(), Body.Trim(), expires, actor, DateTimeOffset.Now), token); await sync.SyncTableAsync(CacheTable.Announcements, token); Title = Body = string.Empty; await LoadAsync(token); } catch (ServerDeniedException) { Error = "Your role changed."; windows.CloseAdminWindow(); } }
    [RelayCommand] private async Task DeleteAsync(Announcement item, CancellationToken token) { if (!windows.Confirm($"Delete the announcement '{item.Title}'?", "Delete announcement")) return; await gateway.DeleteAsync(CacheTable.Announcements, item.Id, token); await sync.SyncTableAsync(CacheTable.Announcements, token); await LoadAsync(token); }
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

public partial class UserEditorItem : ObservableObject { public Guid Id { get; init; } public string DisplayName { get; init; } = string.Empty; public UserRole OriginalRole { get; set; } public bool OriginalIsActive { get; set; } [ObservableProperty] private UserRole _role; [ObservableProperty] private bool _isActive; [ObservableProperty] private string? _error; public string Email { get; init; } = "Not available"; }
public partial class UsersViewModel(IProfileRepository profiles, ISupabaseGateway gateway, ISyncService sync, ISessionService session, IWindowService windows) : ObservableObject
{
    public ObservableCollection<UserEditorItem> Items { get; } = [];
    public IReadOnlyList<UserRole> Roles { get; } = Enum.GetValues<UserRole>();
    public async Task LoadAsync(CancellationToken token = default) { Items.Clear(); foreach (Profile p in (await profiles.GetAllAsync(token)).OrderByDescending(x => x.Id == session.Current.UserId).ThenByDescending(x => x.Role == UserRole.Admin).ThenBy(x => x.DisplayName)) Items.Add(new() { Id = p.Id, DisplayName = p.DisplayName, Role = p.Role, OriginalRole = p.Role, IsActive = p.IsActive, OriginalIsActive = p.IsActive, Email = p.Id == session.Current.UserId ? session.Current.Email ?? "Not available" : "Not stored (MVP)" }); }
    [RelayCommand] private async Task SaveAsync(UserEditorItem item, CancellationToken token) { bool changed = item.Role != item.OriginalRole || item.IsActive != item.OriginalIsActive; if (!changed) return; if (!windows.Confirm($"Apply the role and account-status changes for {item.DisplayName}?", "Update user")) return; try { await gateway.UpdateProfileAsync(item.Id, item.Role == UserRole.Admin ? "admin" : "staff", item.IsActive, token); await sync.SyncTableAsync(CacheTable.Profiles, token); item.OriginalRole = item.Role; item.OriginalIsActive = item.IsActive; item.Error = null; } catch (LastAdminException) { item.Error = "You cannot demote or deactivate the last active admin. Promote someone else first."; } catch (ServerDeniedException) { item.Error = "Your role changed."; windows.CloseAdminWindow("Your role changed. The admin editor has been closed."); } }
}
