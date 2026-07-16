using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using AqiClock.App.ViewModels;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using CommunityToolkit.Mvvm.Messaging;
using H.NotifyIcon;

namespace AqiClock.App.Services;

public sealed class TrayService : IRecipient<SessionChanged>, IDisposable
{
    private readonly MainViewModel _main;
    private readonly ISessionService _session;
    private readonly ISyncService _sync;
    private readonly IWindowService _windows;
    private TaskbarIcon? _icon;
    private bool _disposed;

    public TrayService(MainViewModel main, ISessionService session, ISyncService sync, IWindowService windows, IMessenger messenger)
    {
        _main = main; _session = session; _sync = sync; _windows = windows;
        messenger.Register(this);
        main.Clock.PropertyChanged += OnClockChanged;
    }

    public void Start()
    {
        if (_session.Current.UserId is not null) Show();
    }

    public void Receive(SessionChanged message)
    {
        if (message.State.UserId is null) Hide(); else Show();
    }

    private void Show()
    {
        if (_icon is not null) return;
        var menu = new ContextMenu();
        menu.Opened += (_, _) => BuildMenu(menu);
        _icon = new TaskbarIcon
        {
            ToolTipText = BuildTooltip(),
            ContextMenu = menu,
        };
        ApplyNativeIcon(_icon);
        _icon.TrayMouseDoubleClick += (_, _) => _windows.ShowMainWindow();
        _icon.ForceCreate(false);
    }

    internal static void ApplyNativeIcon(TaskbarIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        icon.Icon = (Icon)SystemIcons.Information.Clone();
    }

    private void BuildMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(Item("Open", (_, _) => _windows.ShowMainWindow()));
        menu.Items.Add(Item("Compact mode", (_, _) => _main.ToggleDisplayModeCommand.Execute(null), _main.DisplayMode == DisplayMode.Compact, checkable: true));
        menu.Items.Add(Item("Always on top", (_, _) => _main.TogglePinCommand.Execute(null), _main.AlwaysOnTop, checkable: true));
        menu.Items.Add(Item($"Announcements ({_main.Announcements.UnreadCount})", (_, _) => _windows.ShowAnnouncements()));
        menu.Items.Add(Item("Sync now", async (_, _) => await _sync.SyncAllAsync(), enabled: _main.IsOnline));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings", (_, _) => _windows.ShowSettingsWindow()));
        menu.Items.Add(Item("Sign out", async (_, _) => { await _session.SignOutAsync(); _windows.ShowSignInWindow(); }));
        menu.Items.Add(Item("Exit", (_, _) => _windows.ExitApplication()));
    }

    private static MenuItem Item(string header, RoutedEventHandler handler, bool isChecked = false, bool enabled = true, bool checkable = false)
    {
        var item = new MenuItem { Header = header, IsCheckable = checkable, IsChecked = isChecked, IsEnabled = enabled };
        item.Click += handler;
        return item;
    }

    private string BuildTooltip()
    {
        string text = _main.Clock.HasCurrentLesson
            ? $"{_main.Clock.CurrentLesson} — {_main.Clock.Remaining} left"
            : $"No lesson · {_main.Clock.NextLesson.ToLowerInvariant()}";
        return text.Length <= 63 ? text : text[..60] + "…";
    }

    private void OnClockChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_icon is null || args.PropertyName is not (nameof(ClockViewModel.CurrentLesson) or nameof(ClockViewModel.Remaining) or nameof(ClockViewModel.NextLesson))) return;
        string text = BuildTooltip();
        if (!string.Equals(_icon.ToolTipText, text, StringComparison.Ordinal)) _icon.ToolTipText = text;
    }

    private void Hide() { _icon?.Dispose(); _icon = null; }

    public void Dispose()
    {
        if (_disposed) return;
        _main.Clock.PropertyChanged -= OnClockChanged;
        Hide(); _disposed = true;
    }
}
