using System.IO;
using System.IO.Pipes;

namespace AqiClock.App.Services;

public static class ActivationPipe
{
    private const string PipeName = "AqiClock.Activation";
    private const int MaximumMessageLength = 16 * 1024;

    public static async Task<bool> TrySendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaximumMessageLength) return false;
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(2000, cancellationToken).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe, leaveOpen: false);
            await writer.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException) { return false; }
        catch (TimeoutException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static async Task ListenAsync(
        Func<string, Task> onMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onMessage);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(pipe);
                string? message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(message) && message.Length <= MaximumMessageLength)
                    await onMessage(message).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }
}
