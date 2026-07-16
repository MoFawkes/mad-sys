using System.Windows;
using AqiClock.Application.Abstractions;
using AqiClock.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AqiClock.App.Services;

public sealed class WindowService(IServiceProvider services) : IWindowService
{
    private SignInWindow? _signIn;
    public void ShowMainWindow() { MainWindow window = services.GetRequiredService<MainWindow>(); System.Windows.Application.Current.MainWindow = window; window.Show(); window.Activate(); }
    public void ShowSignInWindow() { MainWindow? main = services.GetService<MainWindow>(); if (main?.IsVisible == true) main.Hide(); _signIn ??= services.GetRequiredService<SignInWindow>(); _signIn.Closed += (_, _) => _signIn = null; System.Windows.Application.Current.MainWindow = _signIn; _signIn.Show(); _signIn.Activate(); }
    public void ShowSettingsWindow() { SettingsWindow window = services.GetRequiredService<SettingsWindow>(); window.Owner = System.Windows.Application.Current.MainWindow; window.ShowDialog(); }
    public void ActivateMainWindow() => ShowMainWindow();
    public void CloseSignInWindow() => _signIn?.Close();
    public void ShutdownApplication() => System.Windows.Application.Current.Shutdown();
}
