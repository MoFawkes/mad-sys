using AqiClock.App.ViewModels;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class SettingsWindow : FluentWindow { public SettingsWindow(SettingsViewModel viewModel) { InitializeComponent(); DataContext = viewModel; Closed += (_, _) => viewModel.Dispose(); } }
