using AqiClock.Application.Abstractions;
using AqiClock.Infrastructure.Auth;

namespace AqiClock.IntegrationTests;

public sealed class DpapiSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AqiClock.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RoundTripEncryptsSessionAndClearRemovesIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.Combine(_directory, "session.bin");
        var store = new DpapiSessionStore(path);
        var expected = new StoredSession("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1));

        await store.SaveAsync(expected);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        StoredSession? actual = await store.LoadAsync();

        Assert.NotNull(actual);
        Assert.Equal(expected.AccessToken, actual.AccessToken);
        Assert.Equal(expected.RefreshToken, actual.RefreshToken);
        Assert.DoesNotContain("access-token", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        await store.ClearAsync();
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
