using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using Supabase.Realtime;
using Supabase.Realtime.PostgresChanges;

namespace AqiClock.Infrastructure.Supabase;

public sealed class SupabaseGateway : ISupabaseGateway, IDisposable
{
    private static readonly Action<ILogger, double, Exception?> LogClockSkew = LoggerMessage.Define<double>(LogLevel.Warning, new EventId(4101, nameof(LogClockSkew)), "System clock differs from Supabase by {ClockSkewMinutes:F1} minutes; authentication may fail");
    private static readonly Action<ILogger, int, Exception?> LogRecoveryLogoutFailed = LoggerMessage.Define<int>(LogLevel.Warning, new EventId(4102, nameof(LogRecoveryLogoutFailed)), "Password was updated, but the temporary recovery session could not be revoked (HTTP {StatusCode})");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly global::Supabase.Client _client;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SupabaseGateway> _logger;
    private string? _accessToken;
    private int _clockChecked;

    public SupabaseGateway(IOptions<SupabaseOptions> options, ILogger<SupabaseGateway> logger)
    {
        SupabaseOptions value = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var uri = new Uri(value.Url, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Supabase must use HTTPS; plain HTTP is allowed only for a loopback local stack.");
        }

        _client = new global::Supabase.Client(value.Url, value.AnonKey, new global::Supabase.SupabaseOptions { AutoConnectRealtime = false, AutoRefreshToken = false });
        // The bundled client falls back to "Authorization: Bearer <api key>"
        // when its own Auth session is empty. Modern sb_publishable_* keys are
        // opaque rather than JWTs, so Realtime must authenticate the socket
        // with its existing apikey query parameter and the user token at join.
        _client.Realtime.GetHeaders = static () => [];
        _httpClient = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Add("apikey", value.AnonKey);
    }

    public async Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("auth/v1/token?grant_type=password", new { email, password }, JsonOptions, cancellationToken).ConfigureAwait(false);
        AuthResponse auth = await ReadAuthResponseAsync(response, cancellationToken).ConfigureAwait(false);
        await SetClientSessionAsync(auth).ConfigureAwait(false);
        return MapSession(auth);
    }

    public async Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        string redirect = Uri.EscapeDataString(PasswordRecoveryLink.RedirectUrl);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"auth/v1/recover?redirect_to={redirect}",
            new { email = email.Trim() },
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        CheckClockSkew(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task CompletePasswordRecoveryAsync(
        string accessToken,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        using var update = new HttpRequestMessage(HttpMethod.Put, "auth/v1/user")
        {
            Content = JsonContent.Create(new { password = newPassword }, options: JsonOptions),
        };
        update.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _httpClient.SendAsync(update, cancellationToken).ConfigureAwait(false);
        CheckClockSkew(response);
        response.EnsureSuccessStatusCode();

        using var logout = new HttpRequestMessage(HttpMethod.Post, "auth/v1/logout?scope=local");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using HttpResponseMessage logoutResponse = await _httpClient.SendAsync(logout, cancellationToken).ConfigureAwait(false);
            if (!logoutResponse.IsSuccessStatusCode)
                LogRecoveryLogoutFailed(_logger, (int)logoutResponse.StatusCode, null);
        }
        catch (HttpRequestException exception)
        {
            LogRecoveryLogoutFailed(_logger, 0, exception);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            LogRecoveryLogoutFailed(_logger, 0, exception);
        }
    }

    public async Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("auth/v1/token?grant_type=refresh_token", new { refresh_token = session.RefreshToken }, JsonOptions, cancellationToken).ConfigureAwait(false);
        AuthResponse auth = await ReadAuthResponseAsync(response, cancellationToken).ConfigureAwait(false);
        await SetClientSessionAsync(auth).ConfigureAwait(false);
        return MapSession(auth);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (_accessToken is null) return;
        using var request = CreateRequest(HttpMethod.Post, "auth/v1/logout");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _accessToken = null;
    }

    public async Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetJsonAsync("rest/v1/profiles?select=org_id&id=eq." + GetCurrentUserId(), cancellationToken).ConfigureAwait(false);
        JsonElement rows = document.RootElement;
        if (rows.GetArrayLength() != 1) throw new InvalidOperationException("The signed-in profile is unavailable or inactive.");
        return rows[0].GetProperty("org_id").GetGuid();
    }

    public async Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default)
    {
        string tableName = TableName(table);
        using JsonDocument document = await GetJsonAsync($"rest/v1/{tableName}?select=*", cancellationToken).ConfigureAwait(false);
        IReadOnlyList<object> rows = table switch
        {
            CacheTable.Organizations => DeserializeRows<OrganizationRow>(document),
            CacheTable.Profiles => DeserializeRows<ProfileRow>(document),
            CacheTable.Timetables => DeserializeRows<TimetableRow>(document),
            CacheTable.Periods => DeserializeRows<PeriodRow>(document),
            CacheTable.WeekSchedule => DeserializeRows<WeekScheduleRow>(document),
            CacheTable.DateOverrides => DeserializeRows<DateOverrideRow>(document),
            CacheTable.Announcements => DeserializeRows<AnnouncementRow>(document),
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return new CacheSnapshot(table, rows, DateTimeOffset.UtcNow);
    }

    public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) => SendWriteAsync(HttpMethod.Post, table, null, row, cancellationToken);

    public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) => SendWriteAsync(HttpMethod.Patch, table, id, row, cancellationToken);

    public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) => SendWriteAsync(HttpMethod.Delete, table, id, null, cancellationToken);

    public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default)
    {
        if (role is null && isActive is null) throw new ArgumentException("At least one profile field must be supplied.");
        var row = new Dictionary<string, object>();
        if (role is not null) row["role"] = role;
        if (isActive is not null) row["is_active"] = isActive.Value;
        return SendRequestExpectingOneAsync(HttpMethod.Patch, $"rest/v1/profiles?id=eq.{id}", row, cancellationToken);
    }

    public Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default)
    {
        if (weekday is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(weekday));
        return SendRequestExpectingOneAsync(HttpMethod.Patch, $"rest/v1/week_schedule?weekday=eq.{weekday}",
            new Dictionary<string, object?> { ["timetable_id"] = timetableId }, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        using JsonDocument document = await GetJsonAsync($"rest/v1/audit_log?select=id,actor_id,action,entity_type,entity_id,before,after,created_at&order=created_at.desc&limit={Math.Min(limit, 100)}", cancellationToken).ConfigureAwait(false);
        return document.RootElement.EnumerateArray().Select(row => new AuditEntry(
            row.GetProperty("id").GetInt64(),
            row.GetProperty("actor_id").ValueKind == JsonValueKind.Null ? null : row.GetProperty("actor_id").GetGuid(),
            row.GetProperty("action").GetString() ?? string.Empty,
            row.GetProperty("entity_type").GetString() ?? string.Empty,
            row.GetProperty("entity_id").GetGuid(),
            JsonNode.Parse(row.GetProperty("before").GetRawText()) as JsonObject,
            JsonNode.Parse(row.GetProperty("after").GetRawText()) as JsonObject,
            row.GetProperty("created_at").GetDateTimeOffset())).ToArray();
    }

    public async Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        cancellationToken.ThrowIfCancellationRequested();
        if (_accessToken is null) throw new InvalidOperationException("A session is required before subscribing to Realtime.");
        _client.Realtime.SetAuth(_accessToken);
        await _client.Realtime.ConnectAsync().ConfigureAwait(false);

        var subscriptions = new List<RealtimeChannel>
        {
            await SubscribeTableAsync<RealtimeTimetable>(CacheTable.Timetables, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimePeriod>(CacheTable.Periods, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimeWeekSchedule>(CacheTable.WeekSchedule, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimeDateOverride>(CacheTable.DateOverrides, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimeAnnouncement>(CacheTable.Announcements, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimeProfile>(CacheTable.Profiles, onChange).ConfigureAwait(false),
        };
        return new RealtimeSubscription(subscriptions);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<RealtimeChannel> SubscribeTableAsync<T>(CacheTable table, Func<TableChangeSignal, CancellationToken, Task> onChange) where T : BaseModel, new()
    {
        return await _client.From<T>().On(PostgresChangesOptions.ListenType.All, (_, _) => _ = onChange(new TableChangeSignal(table), CancellationToken.None)).ConfigureAwait(false);
    }

    private async Task SendWriteAsync(HttpMethod method, CacheTable table, Guid? id, object? row, CancellationToken cancellationToken)
    {
        EnsureEditable(table);
        string path = $"rest/v1/{TableName(table)}" + (id is null ? string.Empty : $"?id=eq.{id}");
        using HttpRequestMessage request = CreateRequest(method, path);
        request.Headers.Add("Prefer", "return=minimal");
        if (row is not null) request.Content = JsonContent.Create(row, row.GetType(), options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        CheckClockSkew(response);
        await EnsureWriteSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRequestAsync(HttpMethod method, string path, object row, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(method, path);
        request.Headers.Add("Prefer", "return=minimal");
        request.Content = JsonContent.Create(row, row.GetType(), options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        CheckClockSkew(response);
        await EnsureWriteSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRequestExpectingOneAsync(HttpMethod method, string path, object row, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(method, path);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(row, row.GetType(), options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        CheckClockSkew(response);
        await EnsureWriteSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using JsonDocument result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.RootElement.ValueKind != JsonValueKind.Array || result.RootElement.GetArrayLength() != 1)
            throw new ServerWriteException("The server did not update the expected row.", null);
    }

    private static async Task EnsureWriteSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        PostgrestError? error = null;
        try { error = JsonSerializer.Deserialize<PostgrestError>(body, JsonOptions); } catch (JsonException) { }
        string message = error?.Message ?? $"The server rejected the change ({(int)response.StatusCode}).";
        throw error?.Code switch
        {
            "23503" => new ReferencedRowException(message),
            "23505" => new DuplicateRowException(message),
            "23514" => new LastAdminException(message),
            "42501" => new ServerDeniedException(message, error.Code),
            _ when response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized => new ServerDeniedException(message, error?.Code),
            _ => new ServerWriteException(message, error?.Code),
        };
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, path);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        CheckClockSkew(response);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (_accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    private async Task<AuthResponse> ReadAuthResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        CheckClockSkew(response);
        response.EnsureSuccessStatusCode();
        AuthResponse? auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return auth ?? throw new InvalidOperationException("Supabase returned an empty authentication response.");
    }

    private async Task SetClientSessionAsync(AuthResponse response)
    {
        _accessToken = response.AccessToken;
        await _client.Auth.SetSession(response.AccessToken, response.RefreshToken, false).ConfigureAwait(false);
    }

    private static AuthenticatedSession MapSession(AuthResponse response) => new(response.User.Id, response.User.Email, response.AccessToken, response.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn));

    private Guid GetCurrentUserId()
    {
        string token = _accessToken ?? throw new InvalidOperationException("A session is required.");
        string[] segments = token.Split('.');
        if (segments.Length != 3) throw new InvalidOperationException("The access token is malformed.");
        string payload = segments[1].Replace('-', '+').Replace('_', '/').PadRight((segments[1].Length + 3) / 4 * 4, '=');
        using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(document.RootElement.GetProperty("sub").GetString()!);
    }

    private void CheckClockSkew(HttpResponseMessage response)
    {
        if (Interlocked.Exchange(ref _clockChecked, 1) != 0 || response.Headers.Date is not { } serverTime) return;
        TimeSpan skew = (DateTimeOffset.UtcNow - serverTime).Duration();
        if (skew > TimeSpan.FromMinutes(3)) LogClockSkew(_logger, skew.TotalMinutes, null);
    }

    private static object[] DeserializeRows<T>(JsonDocument document) where T : notnull =>
        document.RootElement.EnumerateArray().Select(element => (object)(element.Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException($"Invalid {typeof(T).Name} response."))).ToArray();

    private static void EnsureEditable(CacheTable table)
    {
        if (table is not (CacheTable.Timetables or CacheTable.Periods or CacheTable.WeekSchedule or CacheTable.DateOverrides or CacheTable.Announcements))
            throw new InvalidOperationException($"{table} is not editable through the client gateway.");
    }

    private static string TableName(CacheTable table) => table switch
    {
        CacheTable.Organizations => "organizations", CacheTable.Profiles => "profiles", CacheTable.Timetables => "timetables", CacheTable.Periods => "periods", CacheTable.WeekSchedule => "week_schedule", CacheTable.DateOverrides => "date_overrides", CacheTable.Announcements => "announcements", _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private sealed record AuthResponse([property: JsonPropertyName("access_token")] string AccessToken, [property: JsonPropertyName("refresh_token")] string RefreshToken, [property: JsonPropertyName("expires_in")] int ExpiresIn, AuthUser User);
    private sealed record AuthUser(Guid Id, string Email);
    private sealed record PostgrestError(string? Code, string? Message);

    private sealed class RealtimeSubscription(IReadOnlyList<RealtimeChannel> channels) : IRealtimeSubscription
    {
        public async ValueTask DisposeAsync()
        {
            foreach (RealtimeChannel channel in channels) channel.Unsubscribe();
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    [Table("timetables")] private sealed class RealtimeTimetable : BaseModel;
    [Table("periods")] private sealed class RealtimePeriod : BaseModel;
    [Table("week_schedule")] private sealed class RealtimeWeekSchedule : BaseModel;
    [Table("date_overrides")] private sealed class RealtimeDateOverride : BaseModel;
    [Table("announcements")] private sealed class RealtimeAnnouncement : BaseModel;
    [Table("profiles")] private sealed class RealtimeProfile : BaseModel;
}
