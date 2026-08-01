using AqiClock.App.ViewModels;
using Wpf.Ui.Controls;
using AqiClock.Application.Abstractions;
using AqiClock.App.Services;
using Microsoft.Extensions.Logging;

namespace AqiClock.App.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel, ISettingsService settings, ILogger<WindowPlacementController> logger)
    {
        InitializeComponent(); DataContext = viewModel;
        _ = new WindowPlacementController(this, settings, value => value.SettingsPlacement, (value, placement) => value with { SettingsPlacement = placement }, logger);
        Closed += (_, _) => viewModel.Dispose();
    }
}
