using System.Windows;
using AqiClock.App.ViewModels;
namespace AqiClock.App.Views;
public partial class SignInWindow : Window
{
    private readonly SignInViewModel _viewModel;
    public SignInWindow(SignInViewModel viewModel) { InitializeComponent(); _viewModel = viewModel; DataContext = viewModel; }
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = Password.Password;
}
