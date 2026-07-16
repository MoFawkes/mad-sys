using System.Windows.Threading;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Domain.Time;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.App.Services;

public sealed class ClockService : IClockService
{
    private readonly DispatcherTimer _timer;
    private readonly IClock _clock;
    private readonly IMessenger _messenger;
    private DateTime? _previous;

    public ClockService(IClock clock, IMessenger messenger)
    {
        _clock = clock;
        _messenger = messenger;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public void Start() { OnTick(this, EventArgs.Empty); _timer.Start(); }
    public void StopClock() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        DateTime now = _clock.Now;
        if (_previous is { } previous && (now - previous).Duration() > TimeSpan.FromSeconds(5))
            _messenger.Send(new TimeJumped(previous, now));
        _previous = now;
        _messenger.Send(new ClockTick(now));
    }
}
