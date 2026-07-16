using System.Windows;
using AqiClock.Application.Abstractions;
using AqiClock.App.Views;
using AqiClock.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AqiClock.App.Services;

public sealed class WindowService(IServiceProvider services, ISessionService session) : IWindowService
{
    private MainWindow? _main;
    private SignInWindow? _signIn;
    private SettingsWindow? _settings;
    public void ShowMainWindow() { _main ??= services.GetRequiredService<MainWindow>(); System.Windows.Application.Current.MainWindow = _main; _main.Show(); _main.Activate(); }
    public void ShowSignInWindow()
    {
        _settings?.Close();
        if (_main?.IsVisible == true) _main.Hide();
        if (_signIn is null)
        {
            _signIn = services.GetRequiredService<SignInWindow>();
            _signIn.Closed += OnSignInClosed;
        }
        System.Windows.Application.Current.MainWindow = _signIn; _signIn.Show(); _signIn.Activate();
    }
    public void ShowSettingsWindow() { _settings = services.GetRequiredService<SettingsWindow>(); _settings.Owner = System.Windows.Application.Current.MainWindow; _settings.ShowDialog(); _settings = null; }
    public void ShowAnnouncements() { ShowMainWindow(); services.GetRequiredService<MainViewModel>().IsAnnouncementsOpen = true; }
    public void HideMainWindow() => _main?.Hide();
    public void ActivateMainWindow() => ShowMainWindow();
    public void CloseSignInWindow() => _signIn?.Close();
    public void ShutdownApplication() => System.Windows.Application.Current.Shutdown();
    public void ExitApplication() { _main?.AllowClose(); System.Windows.Application.Current.Shutdown(); }

    private void OnSignInClosed(object? sender, EventArgs e)
    {
        _signIn = null;
        if (WindowLifecycle.ShouldExitAfterSignInClose(session.Current)) System.Windows.Application.Current.Shutdown();
    }
}
