using System.Text.Json;
using System.IO;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using Microsoft.Extensions.Options;

namespace AqiClock.App.Services;

public sealed class SettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsService(IOptions<AqiClockOptions> options)
    {
        string directory = options.Value.DataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqiClock");
        _path = Path.Combine(directory, "settings.json");
    }

    public AppSettings Current { get; private set; } = new();
    public event EventHandler<SettingsChanged>? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return;
        await using FileStream stream = File.OpenRead(_path);
        Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? new();
        Validate(Current);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            await using (FileStream stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, true);
            Current = settings;
            Changed?.Invoke(this, new SettingsChanged(settings));
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Dispose() => _saveGate.Dispose();

    private static void Validate(AppSettings settings)
    {
        if (settings.EndWarningMinutes is < 0 or > 15)
            throw new InvalidDataException("End-warning minutes must be between 0 and 15.");
    }
}
