using System.Collections.ObjectModel;
using AqiClock.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net.Http;

namespace AqiClock.App.ViewModels;

public partial class StudentClassChoice(Guid id, string name) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isSelected;
}

public partial class StudentNaseehahChoice(SessionHalfDay halfDay, string name) : ObservableObject
{
    public SessionHalfDay HalfDay { get; } = halfDay;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isSelected;
}

public partial class StudentClassPickerViewModel(
    IClassRepository classes,
    ILocalCache cache,
    IDeviceAudienceContext audience,
    ISessionService session,
    ISyncService sync) : ObservableObject
{
    public ObservableCollection<StudentClassChoice> Classes { get; } = [];
    public ObservableCollection<StudentNaseehahChoice> NaseehahChoices { get; } =
    [
        new(SessionHalfDay.Am, "Naseehah (AM)"),
        new(SessionHalfDay.Pm, "Naseehah (PM)"),
    ];
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _joinCode = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsEnrollment))]
    private bool _isEnrolled;
    [ObservableProperty] private bool _isBusy;
    public bool NeedsEnrollment => !IsEnrolled;

    public async Task LoadAsync(CancellationToken token = default)
    {
        await cache.InitializeAsync(token);
        IsEnrolled = session.Current.IsAnonymous;
        Classes.Clear();
        foreach (AqiClock.Domain.Entities.Class item in await classes.GetAllAsync(token))
            Classes.Add(new(item.Id, item.Name));
        Error = IsEnrolled && Classes.Count == 0 ? "No classes are available yet. Check the connection and try again." : null;
    }

    public async Task<bool> EnrollAsync(CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(JoinCode)) { Error = "Enter the student-device join code."; return false; }
        IsBusy = true;
        try
        {
            await session.EnrollStudentDeviceAsync(JoinCode, token);
            IsEnrolled = true;
            await sync.StartAsync(token);
            await LoadAsync(token);
            Error = null;
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or ServerWriteException or AuthenticationRejectedException)
        {
            Error = "That join code could not be used. Check the code and connection, then try again.";
            return false;
        }
        finally { IsBusy = false; }
    }

    public async Task<bool> TryStartSessionAsync(CancellationToken token = default)
    {
        Guid[] selected = Classes.Where(x => x.IsSelected).Select(x => x.Id).ToArray();
        if (selected.Length == 0) { Error = "Select at least one class."; return false; }
        SessionHalfDay[] optedHalfDays = NaseehahChoices
            .Where(x => x.IsSelected)
            .Select(x => x.HalfDay)
            .ToArray();
        await audience.SetStudentAsync(selected, optedHalfDays, token);
        Error = null;
        return true;
    }
}
