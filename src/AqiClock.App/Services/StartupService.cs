using AqiClock.Application.Abstractions;
using Microsoft.Win32;
using Velopack.Locators;

namespace AqiClock.App.Services;

public sealed class StartupService(ISettingsService settings) : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AqiClock";
    private bool _disposed;

    public void Start()
    {
        Apply(settings.Current.StartWithWindows);
        settings.Changed += OnSettingsChanged;
    }

    private static void Apply(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (!enabled) { key.DeleteValue(ValueName, false); return; }
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable.");
        string? root = VelopackLocator.IsCurrentSet ? VelopackLocator.Current.RootAppDir : null;
        string? relativeExecutable = VelopackLocator.IsCurrentSet ? VelopackLocator.Current.ThisExeRelativePath : null;
        string executable = StartupPathResolver.Resolve(processPath, root, relativeExecutable);
        key.SetValue(ValueName, $"\"{executable}\" --minimized", RegistryValueKind.String);
    }

    private void OnSettingsChanged(object? sender, SettingsChanged args) => Apply(args.Settings.StartWithWindows);

    public void Dispose()
    {
        if (_disposed) return;
        settings.Changed -= OnSettingsChanged;
        _disposed = true;
    }
}
