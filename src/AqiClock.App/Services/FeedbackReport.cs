using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AqiClock.App.Services;

public sealed record FeedbackDiagnostics(string AppVersion, string Platform, string OsVersion, string Architecture,
    string SessionMode, string TimeZone, string Appearance, string SyncStatus, string? LastSyncedAt,
    IReadOnlyList<string> SelectedClassIds, string NotificationHealth, bool TaskbarEnabled);
public sealed record FeedbackAttachment(string Name, string FullPath, long Size);
public sealed record FeedbackFile(string Name, string ArchivePath, long Bytes);
public sealed record FeedbackReport(int FormatVersion, string ReportId, string CreatedAt, string Category,
    string Description, string ExpectedBehavior, FeedbackDiagnostics? Diagnostics, IReadOnlyList<FeedbackFile> Attachments);

public static class FeedbackReports
{
    public const int MaximumFiles = 5;
    public const long MaximumBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    public static string Serialize(FeedbackReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string ArchivePath(string name, int index)
    {
        string basename = name.Replace('\\', '/').Split('/')[^1];
        string safe = string.Concat(basename.Take(100).Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ' ' ? c : '_'));
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..") safe = "attachment";
        return $"attachments/{index + 1:D2}-{safe}";
    }

    public static void ValidateAttachments(IReadOnlyList<FeedbackAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        if (attachments.Count > MaximumFiles) throw new InvalidOperationException("Choose up to five attachments.");
        long total = 0;
        foreach (FeedbackAttachment attachment in attachments)
        {
            if (attachment.Size < 0 || attachment.Size > MaximumBytes - total)
                throw new InvalidOperationException("Attachments must total 10 MB or less.");
            total += attachment.Size;
        }
    }

    public static async Task<byte[]> CreateArchiveAsync(FeedbackReport report, IReadOnlyList<FeedbackAttachment> attachments, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateAttachments(attachments);
        if (string.IsNullOrWhiteSpace(report.Description) || report.Description.Length > 4000)
            throw new InvalidOperationException("Describe the problem in up to 4,000 characters.");
        if (report.Attachments.Count != attachments.Count) throw new InvalidOperationException("The attachment list changed. Please review it again.");
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            await using (Stream stream = zip.CreateEntry("report.json").Open())
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(Serialize(report).AsMemory(), token);
            await using (Stream stream = zip.CreateEntry("README.txt").Open())
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync("AQI Clock problem report\nOpen report.json for the description and app details.\nThe attachments folder contains only files selected by the reporter.\nSend this ZIP to the person helping with AQI Clock.\n".AsMemory(), token);
            long total = 0;
            byte[] buffer = new byte[65536];
            for (int index = 0; index < attachments.Count; index++)
            {
                FeedbackAttachment attachment = attachments[index];
                string path = ArchivePath(attachment.Name, index);
                if (report.Attachments[index] != new FeedbackFile(attachment.Name, path, attachment.Size))
                    throw new InvalidOperationException("The attachment list changed. Please review it again.");
                await using var input = new FileStream(attachment.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                if (input.Length != attachment.Size) throw new IOException("An attachment changed. Remove it and choose it again.");
                await using Stream target = zip.CreateEntry(path, CompressionLevel.NoCompression).Open();
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    copied += read; total += read;
                    if (total > MaximumBytes || copied > attachment.Size) throw new IOException("An attachment grew beyond the report limit.");
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (copied != attachment.Size) throw new IOException("An attachment changed. Remove it and choose it again.");
            }
        }
        return output.ToArray();
    }
}
