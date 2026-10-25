using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using AqiClock.App.Services;

namespace AqiClock.Application.Tests;

public sealed class TaskbarDisplayTests
{
    [Theory]
    [InlineData(17763, Architecture.X64, true)]
    [InlineData(19045, Architecture.X64, true)]
    [InlineData(22000, Architecture.X64, false)]
    [InlineData(26300, Architecture.X64, false)]
    [InlineData(19045, Architecture.Arm64, false)]
    [InlineData(19045, Architecture.X86, false)]
    public void NativeToolbarIsRestrictedToWindows10X64(int build, Architecture architecture, bool expected) =>
        Assert.Equal(expected, TaskbarRegistration.Supports(new Version(10, 0, build), architecture));

    [Fact]
    public void SignedOutDisplayNeverLeaksCachedLessonDetails()
    {
        (string text, string tooltip) = TaskbarDisplayService.Format(false, true, "Private class", "12:34", "Ends 19:05", "Next private class");
        Assert.Equal("AQI Clock | Sign in", text);
        Assert.DoesNotContain("Private", tooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActiveAndIdleDisplaysUseTheExistingClockStatus()
    {
        Assert.Equal(("Lesson 3 | 12:34 left", "Lesson 3 · 12:34 left · Ends at 19:05 · Next: Lesson 4 at 19:05"),
            TaskbarDisplayService.Format(true, true, "Lesson 3", "12:34", "Ends at 19:05", "Next: Lesson 4 at 19:05"));
        Assert.Equal(("No lessons today", "Next: Monday, Lesson 1 at 18:15"),
            TaskbarDisplayService.Format(true, false, "No lessons today", "", "", "Next: Monday, Lesson 1 at 18:15"));
    }

    [Fact]
    public void SnapshotMatchesNativeLayoutAndBoundsUnicode()
    {
        DateTime utc = new(2026, 10, 17, 18, 52, 0, DateTimeKind.Utc);
        byte[] snapshot = TaskbarSnapshotPublisher.Encode(new string('a', 254) + "😀", "Next:\nLesson\t2" + new string('b', 900), utc);
        Assert.Equal(2064, snapshot.Length);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(snapshot.AsSpan(4)));
        Assert.Equal(utc.ToFileTimeUtc(), BinaryPrimitives.ReadInt64LittleEndian(snapshot.AsSpan(8)));
        string text = Encoding.Unicode.GetString(snapshot, 16, 512).TrimEnd('\0');
        Assert.Equal(new string('a', 254), text);
        string tooltip = Encoding.Unicode.GetString(snapshot, 528, 1536).TrimEnd('\0');
        Assert.StartsWith("Next: Lesson 2", tooltip, StringComparison.Ordinal);
        Assert.Equal(767, tooltip.Length);
        Assert.Equal(0, snapshot[^1]);
        Assert.Equal(0, snapshot[^2]);
    }
}
