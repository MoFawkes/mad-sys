using System.Security.Cryptography;
using System.Text.Json;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using Microsoft.Extensions.Options;

namespace AqiClock.Infrastructure.Auth;

public sealed class DpapiSessionStore : ISessionStore
{
    private static readonly byte[] Entropy = "AqiClock.Session.v1"u8.ToArray();

    public DpapiSessionStore(IOptions<AqiClockOptions> options)
        : this(ResolvePath(options?.Value ?? throw new ArgumentNullException(nameof(options))))
    {
    }

    public DpapiSessionStore(string sessionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionPath);
        SessionPath = Path.GetFullPath(sessionPath);
    }

    public string SessionPath { get; }

    public async Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI session persistence requires Windows.");
        if (!File.Exists(SessionPath)) return null;
        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(SessionPath, cancellationToken).ConfigureAwait(false);
            byte[] clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return JsonSerializer.Deserialize<StoredSession>(clear);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clear);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            File.Delete(SessionPath);
            return null;
        }
    }

    public async Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI session persistence requires Windows.");
        Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            byte[] encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            string temporaryPath = SessionPath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, SessionPath, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(SessionPath)) File.Delete(SessionPath);
        return Task.CompletedTask;
    }

    private static string ResolvePath(AqiClockOptions options)
    {
        string directory = string.IsNullOrWhiteSpace(options.DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqiClock")
            : options.DataDirectory;
        return Path.Combine(directory, "session.bin");
    }

}
