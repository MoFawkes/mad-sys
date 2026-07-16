using System.IO;

namespace AqiClock.App.Services;

public static class StartupPathResolver
{
    public static string Resolve(string processPath, string? velopackRoot, string? relativeExecutable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        if (string.IsNullOrWhiteSpace(velopackRoot) || string.IsNullOrWhiteSpace(relativeExecutable)) return processPath;
        return Path.Combine(velopackRoot, Path.GetFileName(relativeExecutable));
    }
}
