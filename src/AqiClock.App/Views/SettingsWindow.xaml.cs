using System.Windows;
using AqiClock.App.ViewModels;
namespace AqiClock.App.Views;
public partial class SettingsWindow : Window { public SettingsWindow(SettingsViewModel viewModel) { InitializeComponent(); DataContext = viewModel; } }
