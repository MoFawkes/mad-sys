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
    public static ActivationTarget TargetForActivation(SessionState session, bool recoveryVisible) =>
        recoveryVisible ? ActivationTarget.PasswordRecovery :
        session.UserId is null ? ActivationTarget.SignIn :
        ActivationTarget.Main;
}

public enum ActivationTarget { SignIn, PasswordRecovery, Main }

public static class WindowPlacements
{
    public static AppSettings Apply(AppSettings settings, DisplayMode mode, WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WindowPlacement normalized = mode == DisplayMode.Compact
            ? placement with { Width = WindowLayouts.For(DisplayMode.Compact).Width, Height = WindowLayouts.For(DisplayMode.Compact).Height, IsMaximized = false }
            : placement;
        return mode == DisplayMode.Compact
            ? settings with { CompactPlacement = normalized }
            : settings with { NormalPlacement = normalized };
    }
}
