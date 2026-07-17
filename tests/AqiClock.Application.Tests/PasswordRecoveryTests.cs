using System.Net.Http;
using AqiClock.App.Services;
using AqiClock.App.ViewModels;
using AqiClock.Application.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqiClock.Application.Tests;

public sealed class PasswordRecoveryTests
{
    [Fact]
    public void RecoveryLinkAcceptsOnlyTheExpectedSchemeHostAndRecoveryType()
    {
        const string valid = "aqiclock://reset-password/#access_token=test-token&type=recovery";
        Assert.True(PasswordRecoveryLink.TryParse(valid, out PasswordRecoveryRequest? request));
        Assert.Equal("test-token", request!.AccessToken);

        Assert.False(PasswordRecoveryLink.TryParse("https://reset-password/#access_token=x&type=recovery", out _));
        Assert.False(PasswordRecoveryLink.TryParse("aqiclock://other/#access_token=x&type=recovery", out _));
        Assert.False(PasswordRecoveryLink.TryParse("aqiclock://reset-password/#access_token=x&type=invite", out _));
        Assert.False(PasswordRecoveryLink.TryParse("aqiclock://reset-password/#type=recovery", out _));
    }

    [Fact]
    public void ProtocolCommandTargetsTheStableVelopackStubAndQuotesTheUri()
    {
        string stable = ProtocolRegistration.ResolveStableExecutable(
            @"C:\Users\staff\AppData\Local\AqiClock.App\current\AqiClock.App.exe");
        Assert.Equal(@"C:\Users\staff\AppData\Local\AqiClock.App\AqiClock.App.exe", stable);
        Assert.Equal(
            "\"C:\\Users\\staff\\AppData\\Local\\AqiClock.App\\AqiClock.App.exe\" \"%1\"",
            ProtocolRegistration.BuildOpenCommand(stable));
    }

    [Fact]
    public async Task PasswordMismatchNeverCallsTheGateway()
    {
        var gateway = new Gateway();
        var windows = new Windows();
        var viewModel = Create(gateway, windows);
        viewModel.Initialize(new PasswordRecoveryRequest("token"));
        viewModel.NewPassword = "long-enough-password";
        viewModel.ConfirmPassword = "different-password";

        await viewModel.UpdatePasswordCommand.ExecuteAsync(null);

        Assert.False(gateway.WasCalled);
        Assert.Contains("do not match", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(windows.WasClosed);
    }

    [Fact]
    public async Task ValidPasswordIsUpdatedAndRecoveryWindowCloses()
    {
        var gateway = new Gateway();
        var windows = new Windows();
        var viewModel = Create(gateway, windows);
        viewModel.Initialize(new PasswordRecoveryRequest("temporary-token"));
        viewModel.NewPassword = "correct-horse-battery";
        viewModel.ConfirmPassword = "correct-horse-battery";

        await viewModel.UpdatePasswordCommand.ExecuteAsync(null);

        Assert.True(gateway.WasCalled);
        Assert.Equal("temporary-token", gateway.AccessToken);
        Assert.Equal("correct-horse-battery", gateway.Password);
        Assert.True(windows.WasClosed);
        Assert.Empty(viewModel.NewPassword);
        Assert.Empty(viewModel.ConfirmPassword);
    }

    private static PasswordRecoveryViewModel Create(Gateway gateway, Windows windows) =>
        new(gateway, windows, NullLogger<PasswordRecoveryViewModel>.Instance);

    private sealed class Gateway : ISupabaseGateway
    {
        public bool WasCalled { get; private set; }
        public string? AccessToken { get; private set; }
        public string? Password { get; private set; }
        public Task CompletePasswordRecoveryAsync(string accessToken, string newPassword, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            AccessToken = accessToken;
            Password = newPassword;
            return Task.CompletedTask;
        }
        public Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Windows : IWindowService
    {
        public bool WasClosed { get; private set; }
        public void ClosePasswordRecoveryWindow() => WasClosed = true;
        public void ShowPasswordRecoveryWindow(PasswordRecoveryRequest request) { }
        public void ShowMainWindow() { }
        public void ShowSignInWindow() { }
        public void ShowSettingsWindow() { }
        public void ShowAdminWindow() { }
        public void CloseAdminWindow(string? reason = null) { }
        public bool Confirm(string message, string title) => true;
        public void ShowAnnouncements() { }
        public void HideMainWindow() { }
        public void ActivateMainWindow() { }
        public void CloseSignInWindow() { }
        public void ShutdownApplication() { }
        public void ExitApplication() { }
    }
}
