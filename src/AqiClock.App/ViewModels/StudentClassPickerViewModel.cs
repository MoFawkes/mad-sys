using System.Collections.ObjectModel;
using AqiClock.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqiClock.App.ViewModels;

public partial class StudentClassChoice(Guid id, string name) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isSelected;
}

public partial class StudentClassPickerViewModel(
    IClassRepository classes,
    ILocalCache cache,
    IDeviceAudienceContext audience) : ObservableObject
{
    public ObservableCollection<StudentClassChoice> Classes { get; } = [];
    public IReadOnlyList<SessionHalfDay> HalfDays { get; } = Enum.GetValues<SessionHalfDay>();
    [ObservableProperty] private SessionHalfDay _selectedHalfDay = SessionHalfDay.Am;
    [ObservableProperty] private string? _error;

    public async Task LoadAsync(CancellationToken token = default)
    {
        await cache.InitializeAsync(token);
        Classes.Clear();
        foreach (AqiClock.Domain.Entities.Class item in await classes.GetAllAsync(token))
            Classes.Add(new(item.Id, item.Name));
        Error = Classes.Count == 0 ? "No classes are cached on this computer. Ask an administrator to sync it first." : null;
    }

    public bool TryStartSession()
    {
        Guid[] selected = Classes.Where(x => x.IsSelected).Select(x => x.Id).ToArray();
        if (selected.Length == 0) { Error = "Select at least one class."; return false; }
        audience.SetStudent(selected, SelectedHalfDay);
        Error = null;
        return true;
    }
}
