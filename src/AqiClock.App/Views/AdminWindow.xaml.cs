using AqiClock.App.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Data;
using Wpf.Ui.Controls;
using AqiClock.Application.Abstractions;
using AqiClock.App.Services;
using Microsoft.Extensions.Logging;

namespace AqiClock.App.Views;

public partial class AdminWindow : FluentWindow
{
    private readonly AdminViewModel _viewModel;
    public AdminWindow(AdminViewModel viewModel, ISettingsService settings, ILogger<WindowPlacementController> logger) { InitializeComponent(); Navigation.ItemsSource = CreateNavigationItems(); _viewModel = viewModel; DataContext = viewModel; Loaded += OnLoaded; _ = new WindowPlacementController(this, settings, value => value.AdminPlacement, (value, placement) => value with { AdminPlacement = placement }, logger); }

    private static ICollectionView CreateNavigationItems()
    {
        AdminNavigationItem[] items =
        [
            new("SCHEDULE", "Timetables", SymbolRegular.TableSimple20, 0),
            new("SCHEDULE", "Week schedule", SymbolRegular.CalendarLtr20, 1),
            new("SCHEDULE", "Date overrides", SymbolRegular.CalendarEdit20, 2),
            new("SCHEDULE", "Prayer times", SymbolRegular.WeatherMoon20, 3),
            new("PEOPLE & ACCESS", "Classes", SymbolRegular.People20, 4),
            new("PEOPLE & ACCESS", "Student devices", SymbolRegular.Phone20, 5),
            new("PEOPLE & ACCESS", "Staff access", SymbolRegular.ShieldPerson20, 8),
            new("COMMUNICATION", "Announcements", SymbolRegular.Megaphone20, 6),
            new("ACTIVITY", "Recent changes", SymbolRegular.History20, 7),
        ];
        ICollectionView view = CollectionViewSource.GetDefaultView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AdminNavigationItem.Group)));
        return view;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    /// <summary>
    /// An open cell editor counts as an unsaved change straight away. Waiting for the edit to
    /// commit leaves a window in which a sync-driven reload replaces the row underneath the
    /// teacher's cursor, and a value that fails conversion mid-typing never commits at all.
    /// </summary>
    private void OnPeriodBeginningEdit(object sender, DataGridBeginningEditEventArgs e) =>
        _viewModel.Timetables.MarkDirtyCommand.Execute(null);
}

public sealed record AdminNavigationItem(string Group, string Title, SymbolRegular Symbol, int Index);
