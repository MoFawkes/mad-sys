using AqiClock.App.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using AqiClock.Application.Abstractions;
using AqiClock.App.Services;
using Microsoft.Extensions.Logging;

namespace AqiClock.App.Views;

public partial class AdminWindow : FluentWindow
{
    private readonly AdminViewModel _viewModel;
    public AdminWindow(AdminViewModel viewModel, ISettingsService settings, ILogger<WindowPlacementController> logger) { InitializeComponent(); _viewModel = viewModel; DataContext = viewModel; Loaded += OnLoaded; _ = new WindowPlacementController(this, settings, value => value.AdminPlacement, (value, placement) => value with { AdminPlacement = placement }, logger); }
    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    /// <summary>
    /// An open cell editor counts as an unsaved change straight away. Waiting for the edit to
    /// commit leaves a window in which a sync-driven reload replaces the row underneath the
    /// teacher's cursor, and a value that fails conversion mid-typing never commits at all.
    /// </summary>
    private void OnPeriodBeginningEdit(object sender, DataGridBeginningEditEventArgs e) =>
        _viewModel.Timetables.MarkDirtyCommand.Execute(null);
}
