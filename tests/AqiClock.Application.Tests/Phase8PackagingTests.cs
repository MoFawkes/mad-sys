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
}
