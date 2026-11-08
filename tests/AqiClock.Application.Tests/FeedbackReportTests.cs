using System.IO;
using System.IO.Compression;
using AqiClock.App.Services;

namespace AqiClock.Application.Tests;

public sealed class FeedbackReportTests
{
    [Fact]
    public async Task ArchiveKeepsDuplicateAttachmentNamesDistinctAndOmitsSourcePaths()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aqi-feedback-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "first"));
        Directory.CreateDirectory(Path.Combine(directory, "second"));
        try
        {
            string first = Path.Combine(directory, "first", "screenshot.txt");
            string second = Path.Combine(directory, "second", "screenshot.txt");
            await File.WriteAllTextAsync(first, "first screenshot");
            await File.WriteAllTextAsync(second, "second screenshot");
            FeedbackAttachment[] files = [new("screenshot.txt", first, new FileInfo(first).Length), new("screenshot.txt", second, new FileInfo(second).Length)];
            FeedbackReport report = Report(files);
            byte[] bytes = await FeedbackReports.CreateArchiveAsync(report, files);
            using var stream = new MemoryStream(bytes);
            using var zip = new ZipArchive(stream);
            Assert.Equal(4, zip.Entries.Count);
            using var firstReader = new StreamReader(zip.GetEntry("attachments/01-screenshot.txt")!.Open());
            Assert.Equal("first screenshot", await firstReader.ReadToEndAsync());
            using var secondReader = new StreamReader(zip.GetEntry("attachments/02-screenshot.txt")!.Open());
            Assert.Equal("second screenshot", await secondReader.ReadToEndAsync());
            using var jsonReader = new StreamReader(zip.GetEntry("report.json")!.Open());
            string json = await jsonReader.ReadToEndAsync();
            Assert.DoesNotContain(directory, json, StringComparison.Ordinal);
            Assert.DoesNotContain("fullPath", json, StringComparison.Ordinal);
            Assert.DoesNotContain("diagnostics", json, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ArchiveRejectsAttachmentsThatChangedAfterPreview()
    {
        string path = Path.GetTempFileName();
        try
        {
            FeedbackAttachment[] files = [new("changed.txt", path, 0)];
            FeedbackReport report = Report(files);
            await File.WriteAllTextAsync(path, "new content");
            await Assert.ThrowsAsync<IOException>(() => FeedbackReports.CreateArchiveAsync(report, files));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LimitsAndArchivePathsAreEnforced()
    {
        Assert.Throws<InvalidOperationException>(() => FeedbackReports.ValidateAttachments(Enumerable.Range(0, 6).Select(_ => new FeedbackAttachment("file", "unused", 0)).ToArray()));
        Assert.Throws<InvalidOperationException>(() => FeedbackReports.ValidateAttachments([new("large", "unused", FeedbackReports.MaximumBytes + 1)]));
        Assert.Throws<InvalidOperationException>(() => FeedbackReports.ValidateAttachments([new("one", "unused", FeedbackReports.MaximumBytes), new("two", "unused", 1)]));
        Assert.Equal("attachments/01-report.json", FeedbackReports.ArchivePath("../../report.json", 0));
        Assert.Equal("attachments/02-attachment", FeedbackReports.ArchivePath("..", 1));
    }

    private static FeedbackReport Report(IReadOnlyList<FeedbackAttachment> files) => new(1, "test-report", "2026-10-26T18:40:00Z", "Timetable", "Lessons are repeated", "One row per time", null,
        files.Select((file, index) => new FeedbackFile(file.Name, FeedbackReports.ArchivePath(file.Name, index), file.Size)).ToArray());
}
