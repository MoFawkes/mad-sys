using AqiClock.App.ViewModels;
using AqiClock.Application.Abstractions;
using System.Windows;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class PasswordRecoveryWindow : FluentWindow
{
    private readonly PasswordRecoveryViewModel _viewModel;

    public PasswordRecoveryWindow(PasswordRecoveryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void Initialize(PasswordRecoveryRequest request) => _viewModel.Initialize(request);

    private void OnNewPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.NewPassword = NewPassword.Password;

    private void OnConfirmPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.ConfirmPassword = ConfirmPassword.Password;
}
