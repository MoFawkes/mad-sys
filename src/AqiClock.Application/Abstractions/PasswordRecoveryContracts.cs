namespace AqiClock.Application.Abstractions;

public sealed record PasswordRecoveryRequest(string AccessToken);

public static class PasswordRecoveryLink
{
    public const string RedirectUrl = "aqiclock://reset-password";
    private const int MaximumUriLength = 16 * 1024;

    public static bool TryParse(string? value, out PasswordRecoveryRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumUriLength ||
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, "aqiclock", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "reset-password", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Dictionary<string, string> parameters = ParseParameters(uri.Fragment);
        if (!parameters.TryGetValue("type", out string? type) ||
            !string.Equals(type, "recovery", StringComparison.Ordinal) ||
            !parameters.TryGetValue("access_token", out string? accessToken) ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        request = new PasswordRecoveryRequest(accessToken);
        return true;
    }

    private static Dictionary<string, string> ParseParameters(string fragment)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string source = fragment.TrimStart('#');
        foreach (string part in source.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            ReadOnlySpan<char> pair = part.AsSpan();
            int separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            string key = Uri.UnescapeDataString(pair[..separator].ToString());
            string value = Uri.UnescapeDataString(pair[(separator + 1)..].ToString());
            result.TryAdd(key, value);
        }

        return result;
    }
}
