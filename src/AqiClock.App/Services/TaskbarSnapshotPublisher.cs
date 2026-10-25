using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Principal;
using System.Text;

namespace AqiClock.App.Services;

/// <summary>A bounded, current-user/session snapshot. Explorer never calls into the app.</summary>
internal sealed class TaskbarSnapshotPublisher : IDisposable
{
    internal const int Size = 2064;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private int _sequence;

    public TaskbarSnapshotPublisher()
    {
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        _mapping = MemoryMappedFile.CreateNew("Local\\AqiClock.Taskbar.v1." + sid, Size);
        _view = _mapping.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Write);
    }

    public void Publish(string text, string tooltip)
    {
        byte[] payload = Encode(text, tooltip, DateTime.UtcNow);
        int sequence = unchecked(_sequence + 2);
        // Zero is reserved for uninitialised data.
        if (sequence == 0) sequence = 2;
        _view.Write(0, sequence - 1);
        Thread.MemoryBarrier();
        _view.WriteArray(4, payload, 4, Size - 4);
        Thread.MemoryBarrier();
        _view.Write(0, sequence);
        _sequence = sequence;
    }

    internal static byte[] Encode(string text, string tooltip, DateTime utc)
    {
        using var buffer = new MemoryStream(new byte[Size], writable: true);
        using var writer = new BinaryWriter(buffer, Encoding.Unicode);
        writer.Write(0);
        writer.Write(1);
        writer.Write(utc.ToFileTimeUtc());
        WriteText(writer, text, 256);
        WriteText(writer, tooltip, 768);
        return buffer.ToArray();
    }

    private static void WriteText(BinaryWriter writer, string text, int capacity)
    {
        // Never split a surrogate pair or permit line/control characters in shell text.
        string clean = string.Concat(text.Select(character => char.IsControl(character) ? ' ' : character));
        int length = Math.Min(clean.Length, capacity - 1);
        if (length > 0 && char.IsHighSurrogate(clean[length - 1])) length--;
        writer.Write(Encoding.Unicode.GetBytes(clean[..length]));
        writer.Write(new byte[(capacity - length) * 2]);
    }

    public void Dispose() { _view.Dispose(); _mapping.Dispose(); }
}
