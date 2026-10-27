using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using AqiClock.App.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AqiClock.App.Views;

public partial class FeedbackWindow : FluentWindow
{
    private readonly FeedbackDiagnostics _diagnostics;
    private readonly string _reportId = Guid.NewGuid().ToString("D");
    private readonly string _createdAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private readonly ObservableCollection<FeedbackAttachment> _attachments = [];

    public FeedbackWindow(FeedbackDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
        InitializeComponent();
        Category.ItemsSource = new[] { "General", "Timetable", "Notifications", "Listening", "Sign-in", "Display" };
        Category.SelectedIndex = 0;
        Attachments.ItemsSource = _attachments;
        UpdatePreview();
    }

    private FeedbackReport Report() => new(1, _reportId, _createdAt, Category.SelectedItem as string ?? "General",
        Description.Text.Trim(), Expected.Text.Trim(), IncludeDetails.IsChecked == true ? _diagnostics : null,
        _attachments.Select((file, index) => new FeedbackFile(file.Name, FeedbackReports.ArchivePath(file.Name, index), file.Size)).ToArray());

    private void OnChanged(object sender, RoutedEventArgs e) => UpdatePreview();
    private void UpdatePreview() { if (Preview is not null && IncludeDetails is not null) Preview.Text = FeedbackReports.Serialize(Report()); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose screenshots or files for this report", Multiselect = true, CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var additions = picker.FileNames.Where(path => !_attachments.Any(file => string.Equals(file.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                .Select(path => new FeedbackAttachment(Path.GetFileName(path), path, new FileInfo(path).Length)).ToArray();
            FeedbackReports.ValidateAttachments(_attachments.Concat(additions).ToArray());
            foreach (FeedbackAttachment file in additions) _attachments.Add(file);
            Status.Text = string.Empty;
            UpdatePreview();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Status.Text = exception.Message; }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Attachments.SelectedItem is FeedbackAttachment file) _attachments.Remove(file);
        UpdatePreview();
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Description.Text)) { Status.Text = "Please describe the problem first."; return; }
        var picker = new SaveFileDialog { Title = "Save problem report to share", Filter = "ZIP report (*.zip)|*.zip", DefaultExt = ".zip", AddExtension = true,
            FileName = $"aqi-feedback-{DateTime.Now:yyyyMMdd-HHmm}-{_reportId[..8]}.zip" };
        if (picker.ShowDialog(this) != true) return;
        Export.IsEnabled = false;
        try
        {
            byte[] bytes = await FeedbackReports.CreateArchiveAsync(Report(), _attachments.ToArray());
            await File.WriteAllBytesAsync(picker.FileName, bytes);
            Status.Text = $"Saved {Path.GetFileName(picker.FileName)}. Send this ZIP to the person helping with AQI Clock.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Status.Text = $"Could not save the report. {exception.Message}"; }
        finally { Export.IsEnabled = true; }
    }
}
