using AqiClock.Application.Abstractions;

namespace AqiClock.App.Services;

public sealed record WindowLayout(double Width, double Height, double MinimumWidth, double MinimumHeight, bool IsFrameless);

public static class WindowLayouts
{
    public static WindowLayout For(DisplayMode mode) => mode switch
    {
        DisplayMode.Normal => new(720, 760, 520, 540, false),
        DisplayMode.Compact => new(320, 80, 320, 80, true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

public static class WindowLifecycle
{
    public static bool ShouldExitAfterSignInClose(SessionState session) => session.UserId is null;
}
