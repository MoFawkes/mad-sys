using AqiClock.App.ViewModels;
using AqiClock.Application.Abstractions;
using System.Windows;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class SignInWindow : FluentWindow
{
    private readonly SignInViewModel _viewModel;
    private readonly IWindowService _windows;
    public SignInWindow(SignInViewModel viewModel, IWindowService windows) { InitializeComponent(); _viewModel = viewModel; _windows = windows; DataContext = viewModel; }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { _windows.SignInWindowClosing(); base.OnClosing(e); }
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = Password.Password;
}
