using System.Drawing;
using System.Windows;
using AqiClock.App.Services;
using H.NotifyIcon;

namespace AqiClock.Application.Tests;

public sealed class TrayServiceTests
{
    [Fact]
    public async Task SessionTransitionFromWorkerThreadIsMarshalledToStaDispatcher()
    {
        Exception? failure = null;
        int dispatcherThread = 0;
        int callbackThread = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                dispatcherThread = Environment.CurrentManagedThreadId;
                _ = Task.Run(() => TrayService.Dispatch(dispatcher, () =>
                {
                    callbackThread = Environment.CurrentManagedThreadId;
                    completion.SetResult();
                    dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                }));
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception exception) { failure = exception; completion.TrySetResult(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(failure);
        Assert.Equal(dispatcherThread, callbackThread);
    }

    [Fact]
    public async Task TrayUsesNativeIconInsteadOfUnsupportedImageSource()
    {
        Exception? failure = null;
        Icon? nativeIcon = null;
        object? imageSource = null;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var trayIcon = new TaskbarIcon();
                TrayService.ApplyNativeIcon(trayIcon);
                nativeIcon = trayIcon.Icon;
                imageSource = trayIcon.IconSource;
                trayIcon.Dispose();
            }
            catch (Exception exception) { failure = exception; }
            finally { completion.SetResult(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task;

        Assert.Null(failure);
        Assert.NotNull(nativeIcon);
        Assert.Null(imageSource);
    }
}
