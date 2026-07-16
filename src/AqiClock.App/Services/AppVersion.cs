using System.Reflection;

namespace AqiClock.App.Services;

public static class AppVersion
{
    public static string Current => Normalize(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "Development";
        int metadata = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadata >= 0 ? informationalVersion[..metadata] : informationalVersion;
    }
}
