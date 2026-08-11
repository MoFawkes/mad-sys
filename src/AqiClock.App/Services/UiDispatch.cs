using System.Windows.Threading;

namespace AqiClock.App.Services;

public static class UiDispatch
{
    internal static Dispatcher? TestDispatcher { get; set; }

    public static void Run(Action action)
    {
        Dispatcher? dispatcher = TestDispatcher ?? System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || !dispatcher.Thread.IsAlive || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            action();
        else
            _ = dispatcher.BeginInvoke(action);
    }

    public static void Run(Func<Task> action)
    {
        Dispatcher? dispatcher = TestDispatcher ?? System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || !dispatcher.Thread.IsAlive || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            _ = action();
        else
            _ = dispatcher.BeginInvoke(action);
    }
}
