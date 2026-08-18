using AqiClock.App.ViewModels;
using AqiClock.Application.Abstractions;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class StudentClassPickerWindow : FluentWindow
{
    private readonly StudentClassPickerViewModel _viewModel;
    private readonly MainViewModel _main;
    private readonly IWindowService _windows;
    public StudentClassPickerWindow(StudentClassPickerViewModel viewModel, MainViewModel main, IWindowService windows)
    { InitializeComponent(); DataContext = _viewModel = viewModel; _main = main; _windows = windows; }
    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e) => await _viewModel.LoadAsync();
    public Task RefreshAsync() => _viewModel.LoadAsync();
    private async void OnEnroll(object sender, System.Windows.RoutedEventArgs e) => await _viewModel.EnrollAsync();
    private void OnBack(object sender, System.Windows.RoutedEventArgs e)
    {
        _windows.ShowRoleChoiceWindow();
        Close();
    }
    private async void OnStart(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!await _viewModel.TryStartSessionAsync()) return;
        await _main.InitializeAsync();
        _windows.ShowMainWindow();
        Close();
    }
}
