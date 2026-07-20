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
    private readonly IDeviceAudienceContext _audience;
    private MainWindow? _main;
    private SignInWindow? _signIn;
    private RoleChoiceWindow? _roleChoice;
    private StudentClassPickerWindow? _studentPicker;
    private PasswordRecoveryWindow? _passwordRecovery;
    private SettingsWindow? _settings;
    private AdminWindow? _admin;
    private bool _returnToRoleChoiceOnSignInClose;
    public WindowService(IServiceProvider services, ISessionService session, IMessenger messenger, IDeviceAudienceContext audience)
    {
        _services = services;
        _session = session;
        _audience = audience;
        messenger.Register(this);
    }

    public void ShowMainWindow() { _main ??= _services.GetRequiredService<MainWindow>(); System.Windows.Application.Current.MainWindow = _main; _main.Show(); _main.Activate(); }
    public void ShowSignInWindow()
    {
        _returnToRoleChoiceOnSignInClose = false;
        ShowSignInWindowCore();
    }
    public void ShowTeacherSignInWindow()
    {
        _returnToRoleChoiceOnSignInClose = true;
        ShowSignInWindowCore();
    }
    private void ShowSignInWindowCore()
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
    public void ShowRoleChoiceWindow()
    {
        _settings?.Close();
        CloseAdminWindow();
        if (_main?.IsVisible == true) _main.Hide();
        if (_roleChoice is null)
        {
            _roleChoice = _services.GetRequiredService<RoleChoiceWindow>();
            _roleChoice.Closed += OnRoleChoiceClosed;
        }
        System.Windows.Application.Current.MainWindow = _roleChoice;
        _roleChoice.Show(); _roleChoice.Activate();
    }
    public void ShowStudentClassPickerWindow()
    {
        if (_studentPicker is null)
        {
            _studentPicker = _services.GetRequiredService<StudentClassPickerWindow>();
            _studentPicker.Closed += OnStudentPickerClosed;
        }
        _ = _studentPicker.RefreshAsync();
        System.Windows.Application.Current.MainWindow = _studentPicker;
        _studentPicker.Show(); _studentPicker.Activate();
    }
    public void ShowPasswordRecoveryWindow(PasswordRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _settings?.Close();
        CloseAdminWindow();
        if (_main?.IsVisible == true) _main.Hide();
        if (_signIn?.IsVisible == true) _signIn.Hide();
        if (_passwordRecovery is null)
        {
            _passwordRecovery = _services.GetRequiredService<PasswordRecoveryWindow>();
            _passwordRecovery.Closed += OnPasswordRecoveryClosed;
        }
        _passwordRecovery.Initialize(request);
        System.Windows.Application.Current.MainWindow = _passwordRecovery;
        _passwordRecovery.Show();
        _passwordRecovery.Activate();
    }
    public void ClosePasswordRecoveryWindow() => _passwordRecovery?.Close();
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
    public void ActivateMainWindow()
    {
        switch (WindowLifecycle.TargetForActivation(
            _session.Current, _passwordRecovery?.IsVisible == true, _audience.Current.Role == DeviceAudienceRole.StudentDevice))
        {
            case ActivationTarget.PasswordRecovery:
                _passwordRecovery!.Activate();
                break;
            case ActivationTarget.SignIn:
                ShowSignInWindow();
                break;
            case ActivationTarget.Main:
                ShowMainWindow();
                break;
            default:
                throw new InvalidOperationException("Unknown activation target.");
        }
    }
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
        bool returnToRoleChoice = _returnToRoleChoiceOnSignInClose;
        _returnToRoleChoiceOnSignInClose = false;
        if (WindowLifecycle.ShouldExitAfterSignInClose(_session.Current, returnToRoleChoice)) System.Windows.Application.Current.Shutdown();
        else if (returnToRoleChoice && _session.Current.UserId is null) ShowRoleChoiceWindow();
    }

    private void OnRoleChoiceClosed(object? sender, EventArgs e)
    {
        _roleChoice = null;
        bool transitioned = _signIn?.IsVisible == true || _studentPicker?.IsVisible == true;
        if (!transitioned && _session.Current.UserId is null && _audience.Current.Role != DeviceAudienceRole.StudentDevice)
            System.Windows.Application.Current.Shutdown();
    }

    private void OnStudentPickerClosed(object? sender, EventArgs e)
    {
        _studentPicker = null;
        if (_session.Current.UserId is null && _audience.Current.Role != DeviceAudienceRole.StudentDevice
            && !System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
            ShowRoleChoiceWindow();
    }

    private void OnPasswordRecoveryClosed(object? sender, EventArgs e)
    {
        _passwordRecovery = null;
        if (System.Windows.Application.Current.Dispatcher.HasShutdownStarted) return;
        if (_session.Current.UserId is null) ShowSignInWindow();
        else ShowMainWindow();
    }
}
