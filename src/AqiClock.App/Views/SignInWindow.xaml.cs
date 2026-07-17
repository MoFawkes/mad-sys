using AqiClock.App.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class SignInWindow : FluentWindow
{
    private readonly SignInViewModel _viewModel;
    public SignInWindow(SignInViewModel viewModel) { InitializeComponent(); _viewModel = viewModel; DataContext = viewModel; }
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = Password.Password;
}
