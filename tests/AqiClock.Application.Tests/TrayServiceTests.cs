using System.Drawing;
using System.Windows;
using AqiClock.App.Services;
using H.NotifyIcon;

namespace AqiClock.Application.Tests;

public sealed class TrayServiceTests
{
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
