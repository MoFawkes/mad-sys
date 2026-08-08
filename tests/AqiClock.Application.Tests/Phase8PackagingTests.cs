using AqiClock.App.Services;
using AqiClock.Application.Abstractions;

namespace AqiClock.Application.Tests;

public sealed class Phase8PackagingTests
{
    [Theory]
    [InlineData(UpdateStatus.Disabled, null, "Updates unavailable in this build")]
    [InlineData(UpdateStatus.UpToDate, null, "Up to date")]
    [InlineData(UpdateStatus.Downloaded, "0.9.2", "Update downloaded — restarts into v0.9.2")]
    [InlineData(UpdateStatus.Failed, null, "Update check unavailable")]
    public void UpdateStateHasStableAboutText(UpdateStatus status, string? version, string expected)
        => Assert.Equal(expected, new UpdateState(status, version).DisplayText);

    [Theory]
    [InlineData("1.2.3+abc123", "1.2.3")]
    [InlineData("0.9.0-preview.4", "0.9.0-preview.4")]
    [InlineData(null, "Development")]
    public void InformationalVersionIsDisplaySafe(string? value, string expected)
        => Assert.Equal(expected, AppVersion.Normalize(value));

    [Fact]
    public void StartupPathUsesStableVelopackStubWhenInstalled()
    {
        string result = StartupPathResolver.Resolve(
            @"C:\Users\staff\AppData\Local\AqiClock.App\current\AqiClock.App.exe",
            @"C:\Users\staff\AppData\Local\AqiClock.App",
            "AqiClock.App.exe");

        Assert.Equal(@"C:\Users\staff\AppData\Local\AqiClock.App\AqiClock.App.exe", result);
    }

    [Fact]
    public void StartupPathKeepsDevelopmentExecutableWithoutVelopack()
    {
        const string executable = @"C:\repo\bin\AqiClock.App.exe";
        Assert.Equal(executable, StartupPathResolver.Resolve(executable, null, null));
    }

    [Fact]
    public void DownloadedUpdatePromptsAndRequestsAutomaticRestart()
    {
        var updates = new UpdateStub(new(UpdateStatus.Downloaded, "0.13.0"));
        var windows = new WindowStub(confirm: true);
        using var prompt = new UpdateRestartPrompt(updates, windows);

        prompt.Start();

        Assert.True(updates.RestartRequested);
        Assert.True(windows.ShutdownRequested);
        Assert.Equal("Update ready", windows.ConfirmationTitle);
        Assert.Contains("v0.13.0", windows.ConfirmationMessage);
    }

    [Fact]
    public void DeclinedUpdatePromptsOnlyOnceForTheSameVersionPerLaunch()
    {
        var state = new UpdateState(UpdateStatus.Downloaded, "0.13.0");
        var updates = new UpdateStub(state);
        var windows = new WindowStub(confirm: false);
        using var prompt = new UpdateRestartPrompt(updates, windows);

        prompt.Start();
        updates.Raise(state);

        Assert.Equal(1, windows.ConfirmationCount);
        Assert.False(updates.RestartRequested);
        Assert.False(windows.ShutdownRequested);
    }

    private sealed class UpdateStub(UpdateState current) : IUpdateService
    {
        public UpdateState Current { get; } = current;
        public bool RestartRequested { get; private set; }
        public event EventHandler<UpdateState>? StateChanged;
        public void Start() { }
        public void RequestRestartToApply() => RestartRequested = true;
        public void PrepareUpdateOnExit() { }
        public void Raise(UpdateState state) => StateChanged?.Invoke(this, state);
        public void Dispose() { }
    }

    private sealed class WindowStub(bool confirm) : IWindowService
    {
        public int ConfirmationCount { get; private set; }
        public string ConfirmationMessage { get; private set; } = string.Empty;
        public string ConfirmationTitle { get; private set; } = string.Empty;
        public bool ShutdownRequested { get; private set; }
        public bool Confirm(string message, string title)
        {
            ConfirmationCount++;
            ConfirmationMessage = message;
            ConfirmationTitle = title;
            return confirm;
        }
        public void ShutdownApplication() => ShutdownRequested = true;
        public void ShowMainWindow() { }
        public void ShowSignInWindow() { }
        public void ShowPasswordRecoveryWindow(PasswordRecoveryRequest request) { }
        public void ClosePasswordRecoveryWindow() { }
        public void ShowSettingsWindow() { }
        public void ShowAdminWindow() { }
        public void CloseAdminWindow(string? reason = null) { }
        public void ShowAnnouncements() { }
        public void HideMainWindow() { }
        public void ActivateMainWindow() { }
        public void CloseSignInWindow() { }
        public void ExitApplication() { }
    }
}
