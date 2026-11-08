using System.Net.Http;
using System.Text.RegularExpressions;

namespace AqiClock.App.Services;

public static partial class EMasjidLiveStatus
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    public const string PlayerUrl = "https://emasjidlive.co.uk/miniplayer/alqalaminstitute";

    // Read the official player's public page only; never connect to its audio stream.
    public static async Task<bool?> FetchAsync()
    {
        try
        {
            string html = await Client.GetStringAsync(PlayerUrl);
            return ParseAvailability(html);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public static bool? ParseAvailability(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (OfflineLabel().IsMatch(html)) return false;
        if (LiveAudioControl().IsMatch(html)) return true;
        return null; // Unknown markup or an error page must never advertise LIVE.
    }

    [GeneratedRegex(@"<small\b[^>]*\bclass\s*=\s*[""'][^""']*\bstatus-label\b[^""']*[""'][^>]*>\s*Currently\s+Offline\s*</small>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex OfflineLabel();

    [GeneratedRegex(@"<audio\b[^>]*\bid\s*=\s*[""']track[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LiveAudioControl();
}
