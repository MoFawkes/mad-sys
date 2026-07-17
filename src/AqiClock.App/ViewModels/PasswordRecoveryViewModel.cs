using System.Net;
using System.Net.Http;
using AqiClock.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AqiClock.App.ViewModels;

public partial class PasswordRecoveryViewModel(
    ISupabaseGateway gateway,
    IWindowService windows,
    ILogger<PasswordRecoveryViewModel> logger) : ObservableObject
{
    private PasswordRecoveryRequest? _request;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdatePasswordCommand))]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdatePasswordCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdatePasswordCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _progressMessage;

    public void Initialize(PasswordRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        ErrorMessage = null;
        ProgressMessage = null;
    }

    private bool CanUpdatePassword() =>
        !IsBusy &&
        _request is not null &&
        !string.IsNullOrEmpty(NewPassword) &&
        !string.IsNullOrEmpty(ConfirmPassword);

    [RelayCommand(CanExecute = nameof(CanUpdatePassword))]
    private async Task UpdatePasswordAsync(CancellationToken cancellationToken)
    {
        if (_request is null) { ErrorMessage = "This recovery link is invalid."; return; }
        if (NewPassword.Length < 10)
        {
            ErrorMessage = "Use at least 10 characters.";
            return;
        }
        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "The passwords do not match.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        ProgressMessage = "Updating password…";
        try
        {
            await gateway.CompletePasswordRecoveryAsync(
                _request.AccessToken, NewPassword, cancellationToken);
            _request = null;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            windows.ClosePasswordRecoveryWindow();
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            LogRecoveryRejected(logger, exception);
            ErrorMessage = "This recovery link has expired or was already used. Request a new one.";
        }
        catch (HttpRequestException exception)
        {
            LogRecoveryUnavailable(logger, exception);
            ErrorMessage = "No connection — the password could not be updated.";
        }
        finally
        {
            IsBusy = false;
            ProgressMessage = null;
        }
    }

    [RelayCommand]
    private void Cancel() => windows.ClosePasswordRecoveryWindow();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password recovery token was rejected")]
    private static partial void LogRecoveryRejected(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password recovery endpoint was unavailable")]
    private static partial void LogRecoveryUnavailable(ILogger logger, Exception exception);
}
