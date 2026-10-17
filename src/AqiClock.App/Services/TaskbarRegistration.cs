using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace AqiClock.App.Services;

internal static class TaskbarRegistration
{
    internal const string ClassId = "{E3B157CF-B755-48CF-8715-91A40A517D62}";
    private const string ClassKey = @"Software\Classes\CLSID\" + ClassId;
    private const string LaunchKey = @"Software\AQIClock\TaskbarDisplay";
    private const string Category = "{00021492-0000-0000-C000-000000000046}";
    internal static string SourcePath => Path.Combine(AppContext.BaseDirectory, "taskbar", "AqiClock.Taskbar.dll");

    // The native toolbar is an x64 Windows 10 shell extension, not a Windows 11 feature.
    internal static bool Supports(Version version, Architecture architecture) =>
        version.Major == 10 && version.Build is >= 17763 and < 22000 && architecture == Architecture.X64;
    internal static bool IsSupported => Supports(Environment.OSVersion.Version, RuntimeInformation.OSArchitecture);

    internal static void Register()
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Taskbar display requires 64-bit Windows 10.");
        byte[] bytes = File.ReadAllBytes(SourcePath);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqiClock", "taskbar", hash);
        Directory.CreateDirectory(directory);
        string installed = Path.Combine(directory, "AqiClock.Taskbar.dll");
        // Explorer may keep a previous DLL loaded. Each binary has an immutable path
        // outside Velopack's version folders, so app updates cannot replace a loaded DLL.
        if (!File.Exists(installed)) File.WriteAllBytes(installed, bytes);
        else if (!SHA256.HashData(File.ReadAllBytes(installed)).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
            throw new IOException("The installed taskbar component does not match this build.");
        using RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using RegistryKey key = hive.CreateSubKey(ClassKey);
        key.SetValue(null, "AQI Clock");
        using RegistryKey server = key.CreateSubKey("InprocServer32");
        server.SetValue(null, installed);
        server.SetValue("ThreadingModel", "Apartment");
        using RegistryKey category = key.CreateSubKey(@"Implemented Categories\" + Category);
        UpdateLaunchPath(hive);
    }

    internal static void UpdateLaunchPath()
    {
        if (!IsSupported) return;
        using RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        UpdateLaunchPath(hive);
    }

    private static void UpdateLaunchPath(RegistryKey hive)
    {
        using RegistryKey key = hive.CreateSubKey(LaunchKey);
        key.SetValue("AppPath", ProtocolRegistration.ResolveStableExecutable(Path.Combine(AppContext.BaseDirectory, "AqiClock.App.exe")));
    }

    internal static void Unregister()
    {
        using RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        hive.DeleteSubKeyTree(ClassKey, throwOnMissingSubKey: false);
        hive.DeleteSubKeyTree(LaunchKey, throwOnMissingSubKey: false);
    }

    internal static void Uninstall()
    {
        if (IsSupported) NotifyShell(enabled: false, changeVisibility: true);
        Unregister();
    }

    internal static bool NotifyShell(bool enabled, bool changeVisibility)
    {
        object? shell = null;
        try
        {
            Type type = Type.GetTypeFromCLSID(new Guid("E6442437-6C68-4F52-94DD-2CFED267EFB9"), throwOnError: true)!;
            shell = Activator.CreateInstance(type);
            if (shell is not ITrayDeskBand tray) return false;
            _ = tray.DeskBandRegistrationChanged();
            if (!changeVisibility) return true;
            Guid id = new(ClassId);
            return (enabled ? tray.ShowDeskBand(ref id) : tray.HideDeskBand(ref id)) >= 0;
        }
        catch (COMException) { return false; }
        finally { if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
    }

    [ComImport, Guid("6D67E846-5B9C-4DB8-9CBC-DDE12F4254F1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITrayDeskBand
    {
        [PreserveSig] int ShowDeskBand(ref Guid clsid);
        [PreserveSig] int HideDeskBand(ref Guid clsid);
        [PreserveSig] int IsDeskBandShown(ref Guid clsid);
        [PreserveSig] int DeskBandRegistrationChanged();
    }
}
