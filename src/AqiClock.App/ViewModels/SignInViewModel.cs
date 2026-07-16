using System.Net.Http;
using System.Net.Mail;
using AqiClock.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AqiClock.App.ViewModels;

public partial class SignInViewModel(
    ISessionService session,
    ISyncService sync,
    ISupabaseGateway gateway,
    IWindowService windows,
    ILogger<SignInViewModel> logger) : ObservableObject
{
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SignInCommand))] private string _email = string.Empty;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SignInCommand))] private string _password = string.Empty;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SignInCommand))] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _progressMessage;

    private bool CanSignIn() => !IsBusy && IsValidEmail(Email) && !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        IsBusy = true; ErrorMessage = null; ProgressMessage = "Signing in…";
        try
        {
            if (!await AuthenticateAsync(cancellationToken)) return;

            ProgressMessage = "Downloading timetable…";
            if (!await StartInitialSyncAsync(cancellationToken)) return;

            try { windows.ShowMainWindow(); windows.CloseSignInWindow(); }
#pragma warning disable CA1031 // Window activation failures must remain visible in the sign-in window and be logged.
            catch (Exception exception) { LogMainWindowFailed(logger, exception); ErrorMessage = "Signed in, but AQI Clock could not open the main window. Please restart the app."; }
#pragma warning restore CA1031
        }
        finally { IsBusy = false; ProgressMessage = null; }
    }

    private async Task<bool> AuthenticateAsync(CancellationToken cancellationToken)
    {
        try { await session.SignInAsync(Email, Password, cancellationToken); return true; }
        catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
        { LogAuthenticationRejected(logger, exception); ErrorMessage = "Incorrect email or password"; return false; }
        catch (HttpRequestException exception)
        { LogAuthenticationUnavailable(logger, exception); ErrorMessage = "No connection — sign-in requires internet"; return false; }
#pragma warning disable CA1031 // Authentication includes local persistence; failures must remain in-window and be logged.
        catch (Exception exception) when (exception is not OperationCanceledException)
        { LogAuthenticationPersistenceFailed(logger, exception); ErrorMessage = "AQI Clock could not save or load local data during sign-in. Please try again."; return false; }
#pragma warning restore CA1031
    }

    private async Task<bool> StartInitialSyncAsync(CancellationToken cancellationToken)
    {
        try { await sync.StartAsync(cancellationToken); return true; }
#pragma warning disable CA1031 // Initial sync is a UI boundary; the authenticated session remains available for retry.
        catch (Exception exception) when (exception is not OperationCanceledException)
        { LogInitialSyncFailed(logger, exception); ErrorMessage = "Signed in, but the initial timetable download failed. Check your connection and try again."; return false; }
#pragma warning restore CA1031
    }

    [RelayCommand]
    private async Task ForgotPasswordAsync(CancellationToken cancellationToken)
    {
        if (!IsValidEmail(Email)) { ErrorMessage = "Enter your email address first"; return; }
        try { await gateway.SendPasswordResetAsync(Email, cancellationToken); ErrorMessage = "Password reset email sent"; }
        catch (HttpRequestException exception) { LogPasswordResetFailed(logger, exception); ErrorMessage = "No connection — try again later"; }
    }

    private static bool IsValidEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return MailAddress.TryCreate(value, out _);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in authentication was rejected")]
    private static partial void LogAuthenticationRejected(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in authentication endpoint was unavailable")]
    private static partial void LogAuthenticationUnavailable(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Error, Message = "Local session persistence failed during sign-in")]
    private static partial void LogAuthenticationPersistenceFailed(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Error, Message = "Initial sync failed after successful sign-in")]
    private static partial void LogInitialSyncFailed(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Error, Message = "Main window activation failed after successful sign-in and sync")]
    private static partial void LogMainWindowFailed(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Password-reset request failed")]
    private static partial void LogPasswordResetFailed(ILogger logger, Exception exception);
}
