using System.Windows;
using AqiClock.Application.Abstractions;
using AqiClock.App.Views;
using AqiClock.App.ViewModels;
using AqiClock.Application.Messages;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace AqiClock.App.Services;

public sealed class WindowService : IWindowService, IRecipient<SessionChanged>
{
    private readonly IServiceProvider _services;
    private readonly ISessionService _session;
    private MainWindow? _main;
    private SignInWindow? _signIn;
    private SettingsWindow? _settings;
    private AdminWindow? _admin;
    public WindowService(IServiceProvider services, ISessionService session, IMessenger messenger)
    {
        _services = services;
        _session = session;
        messenger.Register(this);
    }

    public void ShowMainWindow() { _main ??= _services.GetRequiredService<MainWindow>(); System.Windows.Application.Current.MainWindow = _main; _main.Show(); _main.Activate(); }
    public void ShowSignInWindow()
    {
        _settings?.Close();
        CloseAdminWindow();
        if (_main?.IsVisible == true) _main.Hide();
        if (_signIn is null)
        {
            _signIn = _services.GetRequiredService<SignInWindow>();
            _signIn.Closed += OnSignInClosed;
        }
        System.Windows.Application.Current.MainWindow = _signIn; _signIn.Show(); _signIn.Activate();
    }
    public void ShowSettingsWindow() { _settings = _services.GetRequiredService<SettingsWindow>(); _settings.Owner = System.Windows.Application.Current.MainWindow; _settings.ShowDialog(); _settings = null; }
    public void ShowAdminWindow() { if (_session.Current.Role != UserRole.Admin) return; _admin ??= _services.GetRequiredService<AdminWindow>(); _admin.Closed += (_, _) => _admin = null; _admin.Owner = _main; _admin.Show(); _admin.Activate(); }
    public void CloseAdminWindow(string? reason = null)
    {
        _admin?.Close(); _admin = null;
        if (!string.IsNullOrWhiteSpace(reason)) MessageBox.Show(_main, reason, "AQI Clock", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    public bool Confirm(string message, string title)
    {
        Window? owner = _admin is not null ? _admin : _main;
        return MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
    public void ShowAnnouncements() { ShowMainWindow(); _services.GetRequiredService<MainViewModel>().IsAnnouncementsOpen = true; }
    public void HideMainWindow() => _main?.Hide();
    public void ActivateMainWindow() => ShowMainWindow();
    public void CloseSignInWindow() => _signIn?.Close();
    public void ShutdownApplication() => System.Windows.Application.Current.Shutdown();
    public void ExitApplication() { _main?.AllowClose(); System.Windows.Application.Current.Shutdown(); }

    public void Receive(SessionChanged message)
    {
        if (message.State.Role == UserRole.Admin || _admin is null) return;
        void CloseForRoleChange() => CloseAdminWindow("Your role changed. The admin editor has been closed.");
        if (System.Windows.Application.Current.Dispatcher.CheckAccess()) CloseForRoleChange();
        else _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(CloseForRoleChange);
    }

    private void OnSignInClosed(object? sender, EventArgs e)
    {
        _signIn = null;
        if (WindowLifecycle.ShouldExitAfterSignInClose(_session.Current)) System.Windows.Application.Current.Shutdown();
    }
}
