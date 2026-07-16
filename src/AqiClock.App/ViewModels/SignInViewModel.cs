using System.Net.Http;
using System.Net.Mail;
using AqiClock.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqiClock.App.ViewModels;

public partial class SignInViewModel(ISessionService session, ISyncService sync, ISupabaseGateway gateway, IWindowService windows) : ObservableObject
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
            await session.SignInAsync(Email, Password, cancellationToken);
            ProgressMessage = "Downloading timetable…";
            await sync.StartAsync(cancellationToken);
            windows.ShowMainWindow(); windows.CloseSignInWindow();
        }
        catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
        { ErrorMessage = "Incorrect email or password"; }
        catch (HttpRequestException) { ErrorMessage = "No connection — sign-in requires internet"; }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException)
        { ErrorMessage = "Incorrect email or password"; }
        finally { IsBusy = false; ProgressMessage = null; }
    }

    [RelayCommand]
    private async Task ForgotPasswordAsync(CancellationToken cancellationToken)
    {
        if (!IsValidEmail(Email)) { ErrorMessage = "Enter your email address first"; return; }
        try { await gateway.SendPasswordResetAsync(Email, cancellationToken); ErrorMessage = "Password reset email sent"; }
        catch (HttpRequestException) { ErrorMessage = "No connection — try again later"; }
    }

    private static bool IsValidEmail(string value)
    {
        try { _ = new MailAddress(value); return true; } catch (FormatException) { return false; }
    }
}
