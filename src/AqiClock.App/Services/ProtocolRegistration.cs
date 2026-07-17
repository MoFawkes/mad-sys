using System.IO;
using Microsoft.Win32;

namespace AqiClock.App.Services;

public static class ProtocolRegistration
{
    public const string Scheme = "aqiclock";
    private const string RegistryPath = @"Software\Classes\" + Scheme;

    public static void Register()
    {
        string executable = ResolveStableExecutable(
            Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable."));
        using RegistryKey protocol = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
        protocol.SetValue(string.Empty, "URL:AQI Clock Protocol", RegistryValueKind.String);
        protocol.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);
        using RegistryKey command = protocol.CreateSubKey(@"shell\open\command", writable: true);
        command.SetValue(string.Empty, BuildOpenCommand(executable), RegistryValueKind.String);
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
    }

    public static string ResolveStableExecutable(string executable)
    {
        string fullPath = Path.GetFullPath(executable);
        DirectoryInfo? directory = Directory.GetParent(fullPath);
        if (directory is null ||
            !string.Equals(directory.Name, "current", StringComparison.OrdinalIgnoreCase) ||
            directory.Parent is null)
        {
            return fullPath;
        }

        return Path.Combine(directory.Parent.FullName, Path.GetFileName(fullPath));
    }

    public static string BuildOpenCommand(string executable) => $"\"{executable}\" \"%1\"";
}
