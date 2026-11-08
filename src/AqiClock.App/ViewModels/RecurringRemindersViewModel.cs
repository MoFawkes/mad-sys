using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqiClock.App.ViewModels;

public partial class ReminderWeekday : ObservableObject
{
    public string Label { get; init; } = string.Empty;
    public int Bit { get; init; }
    [ObservableProperty] private bool _isSelected;
}

public sealed record ReminderDisplay(RecurringReminder Reminder)
{
    public string Title => Reminder.Title;
    public string ToggleLabel => Reminder.IsEnabled ? "Pause" : "Resume";
    public string Summary => $"{(Reminder.IsEnabled ? "Active" : "Paused")} · " +
        string.Join(", ", Enumerable.Range(0, 7).Where(day => (Reminder.Weekdays & (1 << day)) != 0)
            .Select(day => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((day + 1) % 7)))) +
        (Reminder.Timing == ReminderTiming.LessonEnd ? " · After every lesson" : $" · {Reminder.FixedTime:HH:mm} institute time");
}

public partial class RecurringRemindersViewModel(
    ISupabaseGateway gateway, ISyncService sync, IRecurringReminderRepository repository, IWindowService windows) : ObservableObject
{
    private Guid? _editingId;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private bool _afterEveryLesson = true;
    [ObservableProperty] private string _fixedTime = "16:00";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string? _message;
    public ObservableCollection<ReminderDisplay> Items { get; } = [];
    public IReadOnlyList<ReminderWeekday> Weekdays { get; } = Enumerable.Range(0, 7)
        .Select(day => new ReminderWeekday { Label = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((day + 1) % 7)), Bit = 1 << day }).ToArray();

    public async Task LoadAsync(CancellationToken token = default)
    {
        Items.Clear();
        foreach (RecurringReminder reminder in await repository.GetAllAsync(token)) Items.Add(new(reminder));
    }

    [RelayCommand]
    private void Edit(ReminderDisplay item)
    {
        _editingId = item.Reminder.Id;
        Title = item.Reminder.Title; Body = item.Reminder.Body;
        AfterEveryLesson = item.Reminder.Timing == ReminderTiming.LessonEnd;
        FixedTime = item.Reminder.FixedTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "16:00";
        IsEnabled = item.Reminder.IsEnabled;
        foreach (ReminderWeekday day in Weekdays) day.IsSelected = (item.Reminder.Weekdays & day.Bit) != 0;
        Message = "Editing reminder. Save changes or choose New reminder.";
    }

    [RelayCommand]
    private void NewReminder()
    {
        _editingId = null; Title = Body = string.Empty; AfterEveryLesson = IsEnabled = true; FixedTime = "16:00";
        foreach (ReminderWeekday day in Weekdays) day.IsSelected = false;
        Message = null;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken token)
    {
        int days = Weekdays.Where(day => day.IsSelected).Sum(day => day.Bit);
        if (Title.Trim().Length is 0 or > 200 || Body.Trim().Length is 0 or > 2000)
        { Message = "Enter a title (up to 200 characters) and message (up to 2000 characters)."; return; }
        if (days == 0) { Message = "Choose at least one weekday."; return; }
        TimeOnly time = default;
        if (!AfterEveryLesson && !TimeOnly.TryParseExact(FixedTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        { Message = "Enter the fixed time as HH:mm (24-hour institute time)."; return; }
        await WriteAsync(async () =>
        {
            Guid org = await gateway.GetCurrentOrganizationIdAsync(token);
            var row = new RecurringReminderRow(_editingId ?? Guid.NewGuid(), org, Title.Trim(), Body.Trim(), days,
                AfterEveryLesson ? "lesson_end" : "fixed_time", AfterEveryLesson ? null : time, IsEnabled);
            if (_editingId is null) await gateway.InsertAsync(CacheTable.RecurringReminders, row, token);
            else await gateway.UpdateAsync(CacheTable.RecurringReminders, row.Id, row, token);
            NewReminder();
        }, "Reminder saved.", token);
    }

    [RelayCommand]
    private async Task ToggleAsync(ReminderDisplay item, CancellationToken token) => await WriteAsync(async () =>
    {
        RecurringReminder rule = item.Reminder;
        Guid org = await gateway.GetCurrentOrganizationIdAsync(token);
        await gateway.UpdateAsync(CacheTable.RecurringReminders, rule.Id,
            new RecurringReminderRow(rule.Id, org, rule.Title, rule.Body, rule.Weekdays,
                rule.Timing == ReminderTiming.LessonEnd ? "lesson_end" : "fixed_time", rule.FixedTime, !rule.IsEnabled), token);
        if (_editingId == rule.Id) IsEnabled = !rule.IsEnabled;
    }, item.Reminder.IsEnabled ? "Reminder paused." : "Reminder resumed.", token);

    [RelayCommand]
    private async Task DeleteAsync(ReminderDisplay item, CancellationToken token) => await WriteAsync(async () =>
    {
        await gateway.DeleteAsync(CacheTable.RecurringReminders, item.Reminder.Id, token);
        if (_editingId == item.Reminder.Id) NewReminder();
    }, "Reminder deleted.", token);

    private async Task WriteAsync(Func<Task> write, string success, CancellationToken token)
    {
        if (sync.State != ConnectivityState.Online) { Message = "Connect to manage recurring reminders."; return; }
        try
        {
            await write();
            await sync.SyncTableAsync(CacheTable.RecurringReminders, token);
            await LoadAsync(token);
            Message = sync.State == ConnectivityState.Online ? success : $"{success} Local sync is pending; reconnect to refresh this list.";
        }
        catch (ServerDeniedException) { Message = "Your role changed."; windows.CloseAdminWindow(); }
        catch (HttpRequestException) { Message = "Could not save the reminder. Check your connection and try again."; }
    }
}
