using AqiClock.App.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class AdminWindow : FluentWindow
{
    private readonly AdminViewModel _viewModel;
    public AdminWindow(AdminViewModel viewModel) { InitializeComponent(); _viewModel = viewModel; DataContext = viewModel; Loaded += OnLoaded; }
    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();
}
