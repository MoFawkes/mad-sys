using AqiClock.App.ViewModels;
using System.Windows;
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
}
