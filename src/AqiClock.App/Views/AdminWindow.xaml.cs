using System.Windows;
using AqiClock.App.ViewModels;

namespace AqiClock.App.Views;

public partial class AdminWindow : Window
{
    private readonly AdminViewModel _viewModel;
    public AdminWindow(AdminViewModel viewModel) { InitializeComponent(); _viewModel = viewModel; DataContext = viewModel; Loaded += OnLoaded; }
    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();
}
