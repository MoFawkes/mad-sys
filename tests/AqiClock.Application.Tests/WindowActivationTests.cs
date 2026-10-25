using System.Windows;
using System.Windows.Threading;
using AqiClock.App.Services;

namespace AqiClock.Application.Tests;

public sealed class WindowActivationTests
{
    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void ActivationRestoresMinimizedWindowToItsPreviousState(WindowState previous)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                window = new Window { Width = 300, Height = 200, ShowInTaskbar = false };
                window.Show();
                window.WindowState = previous;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.WindowState = WindowState.Minimized;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(WindowState.Minimized, window.WindowState);

                WindowActivation.ShowAndActivate(window);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.True(window.IsVisible);
                Assert.Equal(previous, window.WindowState);
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Window activation test timed out.");
        Assert.Null(failure);
    }
}
