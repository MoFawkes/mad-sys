using System.Diagnostics;
using Serilog;

namespace AqiClock.App.Services;

public sealed class BindingErrorTraceListener : TraceListener
{
    public override void Write(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) Log.Warning("WPF binding error: {BindingError}", message.Trim());
    }

    public override void WriteLine(string? message) => Write(message);
}
