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
        AuthResponse auth = await ReadAuthResponseAsync(response, AuthOperation.PasswordSignIn, cancellationToken).ConfigureAwait(false);
        await SetClientSessionAsync(auth).ConfigureAwait(false);
        return MapSession(auth);
    }

    public async Task<AuthenticatedSession> SignInAnonymouslyAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("auth/v1/signup", new { }, JsonOptions, cancellationToken).ConfigureAwait(false);
        AuthResponse auth = await ReadAuthResponseAsync(response, AuthOperation.AnonymousSignIn, cancellationToken).ConfigureAwait(false);
        await SetClientSessionAsync(auth).ConfigureAwait(false);
        return MapSession(auth);
    }

    public async Task<Guid> EnrollStudentDeviceAsync(string joinCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(joinCode);
        using JsonDocument document = await PostRpcAsync("enroll_student_device", new { join_code = joinCode.Trim().Replace(" ", string.Empty) }, cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetGuid();
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
        AuthResponse auth = await ReadAuthResponseAsync(response, AuthOperation.Refresh, cancellationToken).ConfigureAwait(false);
        await SetClientSessionAsync(auth).ConfigureAwait(false);
        return MapSession(auth);
    }

    public Task RestoreAccessTokenAsync(StoredSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _accessToken = session.AccessToken;
        return Task.CompletedTask;
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
        Guid userId = GetCurrentUserId();
        using JsonDocument document = await GetJsonAsync("rest/v1/profiles?select=org_id&id=eq." + userId, cancellationToken).ConfigureAwait(false);
        JsonElement rows = document.RootElement;
        if (rows.GetArrayLength() == 1) return rows[0].GetProperty("org_id").GetGuid();
        using JsonDocument devices = await GetJsonAsync("rest/v1/student_devices?select=org_id&user_id=eq." + userId, cancellationToken).ConfigureAwait(false);
        if (devices.RootElement.GetArrayLength() == 1) return devices.RootElement[0].GetProperty("org_id").GetGuid();
        throw new InvalidOperationException("The signed-in profile or student-device enrolment is unavailable.");
    }

    public async Task<DateOnly> GetCurrentOrganizationDateAsync(CancellationToken cancellationToken = default)
    {
        Guid orgId = await GetCurrentOrganizationIdAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await GetJsonAsync(
            $"rest/v1/organizations?select=timezone&id=eq.{orgId}", cancellationToken).ConfigureAwait(false);
        if (document.RootElement.GetArrayLength() != 1)
            throw new InvalidOperationException("The current organization is unavailable.");
        string timezone = document.RootElement[0].GetProperty("timezone").GetString()
            ?? throw new InvalidOperationException("The organization timezone is unavailable.");
        DateTime local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(timezone)).DateTime;
        return DateOnly.FromDateTime(local);
    }

    public async Task<string> GetStudentJoinCodeAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await PostRpcAsync(
            "admin_student_join_code", cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetString()
            ?? throw new ServerWriteException("The server returned an empty student join code.", null);
    }

    public async Task<string> RotateStudentJoinCodeAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await PostRpcAsync(
            "rotate_student_join_code", cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetString()
            ?? throw new ServerWriteException("The server returned an empty student join code.", null);
    }

    public async Task<int> RevokeStudentDevicesAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await PostRpcAsync(
            "revoke_student_devices", cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetInt32();
    }

    public async Task<GeneratorAuthoringSnapshot> GetGeneratorAuthoringAsync(Guid timetableId, CancellationToken cancellationToken = default)
    {
        string filter = Uri.EscapeDataString(timetableId.ToString());
        using JsonDocument definitionDocument = await GetJsonAsync($"rest/v1/timetable_generators?select=*&timetable_id=eq.{filter}", cancellationToken).ConfigureAwait(false);
        using JsonDocument blocksDocument = await GetJsonAsync($"rest/v1/timetable_generator_blocks?select=*&timetable_id=eq.{filter}&order=sort_order.asc", cancellationToken).ConfigureAwait(false);
        using JsonDocument anchorsDocument = await GetJsonAsync($"rest/v1/timetable_generator_anchors?select=*&timetable_id=eq.{filter}", cancellationToken).ConfigureAwait(false);
        TimetableGeneratorDefinition? definition = definitionDocument.RootElement.GetArrayLength() == 0
            ? null
            : definitionDocument.RootElement[0].Deserialize<TimetableGeneratorDefinition>(JsonOptions);
        return new(definition, DeserializeRows<TimetableGeneratorBlock>(blocksDocument).Cast<TimetableGeneratorBlock>().ToArray(),
            DeserializeRows<TimetableGeneratorAnchor>(anchorsDocument).Cast<TimetableGeneratorAnchor>().ToArray());
    }

    public async Task<AnchorConfigurationSnapshot> GetAnchorConfigurationAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument anchorsDocument = await GetJsonAsync("rest/v1/organization_anchors?select=*&order=sort_order.asc", cancellationToken).ConfigureAwait(false);
        using JsonDocument standingDocument = await GetJsonAsync("rest/v1/anchor_standing_times?select=*&order=effective_from.asc", cancellationToken).ConfigureAwait(false);
        using JsonDocument overridesDocument = await GetJsonAsync("rest/v1/anchor_date_overrides?select=*&order=date.asc", cancellationToken).ConfigureAwait(false);
        return new(DeserializeRows<OrganizationAnchor>(anchorsDocument).Cast<OrganizationAnchor>().ToArray(),
            DeserializeRows<AnchorStandingTime>(standingDocument).Cast<AnchorStandingTime>().ToArray(),
            DeserializeRows<AnchorDateOverride>(overridesDocument).Cast<AnchorDateOverride>().ToArray());
    }

    public async Task<GeneratorMaintenanceRun> RegenerateGeneratedTimetablesAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await PostRpcAsync("admin_regenerate_generated_timetables", cancellationToken).ConfigureAwait(false);
        return document.RootElement.Deserialize<GeneratorMaintenanceRun>(JsonOptions)
            ?? throw new ServerWriteException("The server returned an empty generator maintenance run.", null);
    }

    public async Task<GeneratorMaintenanceRun?> GetLatestGeneratorMaintenanceRunAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetJsonAsync("rest/v1/generator_maintenance_runs?select=*&order=started_at.desc&limit=1", cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetArrayLength() == 0
            ? null
            : document.RootElement[0].Deserialize<GeneratorMaintenanceRun>(JsonOptions);
    }

    public async Task SaveGeneratedTimetableAsync(Guid timetableId, GeneratorDefinitionWrite definition,
        IReadOnlyList<GeneratorBlockWrite> blocks, IReadOnlyList<Guid> anchorIds,
        IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default)
    {
        using JsonDocument _ = await PostRpcAsync("admin_save_generated_timetable", new
        {
            p_timetable_id = timetableId,
            p_definition = definition,
            p_blocks = blocks,
            p_anchor_ids = anchorIds,
            p_periods = periods,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GeneratorServerPreview> PreviewGeneratedTimetableAsync(Guid timetableId,
        GeneratorDefinitionWrite definition, IReadOnlyList<GeneratorBlockWrite> blocks,
        IReadOnlyList<Guid> anchorIds, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await PostRpcAsync("admin_preview_generated_timetable", new
        {
            p_timetable_id = timetableId,
            p_definition = definition,
            p_blocks = blocks,
            p_anchor_ids = anchorIds,
        }, cancellationToken).ConfigureAwait(false);
        return document.RootElement.Deserialize<GeneratorServerPreview>(JsonOptions)
            ?? throw new ServerWriteException("The server returned an empty generator preview.", null);
    }

    public async Task<int> BulkUpsertAnchorDateOverridesAsync(Guid anchorId,
        IReadOnlyList<AnchorDateOverrideWrite> rows, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await PostRpcAsync("admin_bulk_upsert_anchor_date_overrides",
            new { p_anchor_id = anchorId, p_rows = rows }, cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetInt32();
    }

    public Task SaveAnchorStandingTimeAsync(AnchorStandingTime row, bool isNew, CancellationToken cancellationToken = default) =>
        SendRequestAsync(isNew ? HttpMethod.Post : HttpMethod.Patch,
            "rest/v1/anchor_standing_times" + (isNew ? string.Empty : $"?id=eq.{row.Id}"), row, cancellationToken);

    public Task SaveAnchorDateOverrideAsync(AnchorDateOverride row, bool isNew, CancellationToken cancellationToken = default) =>
        SendRequestAsync(isNew ? HttpMethod.Post : HttpMethod.Patch,
            "rest/v1/anchor_date_overrides" + (isNew ? string.Empty : $"?id=eq.{row.Id}"), row, cancellationToken);

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
            CacheTable.Classes => DeserializeRows<ClassRow>(document),
            CacheTable.PeriodClasses => DeserializeRows<PeriodClassRow>(document),
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

    public async Task SaveTimetableAsync(TimetableRow timetable, IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default)
    {
        using JsonDocument _ = await PostRpcAsync("admin_save_timetable", new { p_timetable = timetable, p_periods = periods }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveWeekScheduleRowAsync(int weekday, Guid? audienceClassId, Guid? timetableId, CancellationToken cancellationToken = default)
    {
        if (weekday is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(weekday));
        var body = new JsonObject
        {
            ["p_weekday"] = weekday,
            ["p_audience_class_id"] = audienceClassId is { } classId ? JsonValue.Create(classId) : null,
            ["p_timetable_id"] = timetableId is { } assignedId ? JsonValue.Create(assignedId) : null,
        };
        using JsonDocument _ = await PostRpcAsync("admin_save_week_schedule", body, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteWeekScheduleRowAsync(int weekday, Guid audienceClassId, CancellationToken cancellationToken = default)
    {
        if (weekday is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(weekday));
        using JsonDocument _ = await PostRpcAsync("admin_delete_week_schedule", new { p_weekday = weekday, p_audience_class_id = audienceClassId }, cancellationToken).ConfigureAwait(false);
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
            await SubscribeTableAsync<RealtimeClass>(CacheTable.Classes, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimePeriodClass>(CacheTable.PeriodClasses, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimeWeekSchedule>(CacheTable.WeekSchedule, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimeDateOverride>(CacheTable.DateOverrides, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimeAnnouncement>(CacheTable.Announcements, onChange).ConfigureAwait(false),
            await SubscribeTableAsync<RealtimeProfile>(CacheTable.Profiles, onChange).ConfigureAwait(false),
        };
        return new RealtimeSubscription(_client.Realtime, subscriptions);
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

    private async Task SendRequestExpectingOneAsync(HttpMethod method, string path, object row, CancellationToken cancellationToken, string prefer = "return=representation")
    {
        using HttpRequestMessage request = CreateRequest(method, path);
        request.Headers.Add("Prefer", prefer);
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

    private Task<JsonDocument> PostRpcAsync(string functionName, CancellationToken cancellationToken) =>
        PostRpcAsync(functionName, new { }, cancellationToken);

    private async Task<JsonDocument> PostRpcAsync(string functionName, object body, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post, $"rest/v1/rpc/{functionName}");
        request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        CheckClockSkew(response);
        await EnsureWriteSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        byte[] payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return payload.Length == 0 ? JsonDocument.Parse("null") : JsonDocument.Parse(payload);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (_accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    private async Task<AuthResponse> ReadAuthResponseAsync(HttpResponseMessage response, AuthOperation operation, CancellationToken cancellationToken)
    {
        CheckClockSkew(response);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string? errorCode = TryReadAuthErrorCode(body);
            if (operation == AuthOperation.Refresh && IsRejectedRefresh(response.StatusCode, errorCode))
                throw new AuthenticationRejectedException("The stored Supabase session is no longer valid.");
            if (operation == AuthOperation.PasswordSignIn && response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
                throw new CredentialRejectedException("The email or password was rejected.");
            throw new HttpRequestException($"Supabase Auth returned HTTP {(int)response.StatusCode} ({errorCode ?? "unknown_error"}).", null, response.StatusCode);
        }
        AuthResponse? auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return auth ?? throw new InvalidOperationException("Supabase returned an empty authentication response.");
    }

    private static string? TryReadAuthErrorCode(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            foreach (string name in (string[])["error_code", "code", "error"])
                if (document.RootElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString()?.ToLowerInvariant();
        }
        catch (JsonException) { }
        return null;
    }

    private static bool IsRejectedRefresh(System.Net.HttpStatusCode statusCode, string? errorCode) =>
        statusCode == System.Net.HttpStatusCode.Unauthorized || errorCode is
            "invalid_grant" or
            "invalid_refresh_token" or
            "validation_failed" or
            "refresh_token_expired" or
            "refresh_token_not_found" or
            "refresh_token_already_used";

    private Task SetClientSessionAsync(AuthResponse response)
    {
        _accessToken = response.AccessToken;
        _client.Realtime.SetAuth(response.AccessToken);
        return Task.CompletedTask;
    }

    private static AuthenticatedSession MapSession(AuthResponse response) => new(response.User.Id, response.User.Email ?? string.Empty, response.AccessToken, response.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn), response.User.IsAnonymous);

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
        if (table is not (CacheTable.Timetables or CacheTable.Periods or CacheTable.Classes or CacheTable.PeriodClasses or CacheTable.WeekSchedule or CacheTable.DateOverrides or CacheTable.Announcements))
            throw new InvalidOperationException($"{table} is not editable through the client gateway.");
    }

    private static string TableName(CacheTable table) => table switch
    {
        CacheTable.Organizations => "organizations", CacheTable.Profiles => "profiles", CacheTable.Timetables => "timetables", CacheTable.Periods => "periods", CacheTable.Classes => "classes", CacheTable.PeriodClasses => "period_classes", CacheTable.WeekSchedule => "week_schedule", CacheTable.DateOverrides => "date_overrides", CacheTable.Announcements => "announcements", _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private sealed record AuthResponse([property: JsonPropertyName("access_token")] string AccessToken, [property: JsonPropertyName("refresh_token")] string RefreshToken, [property: JsonPropertyName("expires_in")] int ExpiresIn, AuthUser User);
    private sealed record AuthUser(Guid Id, string? Email, [property: JsonPropertyName("is_anonymous")] bool IsAnonymous = false);
    private sealed record PostgrestError(string? Code, string? Message);
    private enum AuthOperation { PasswordSignIn, AnonymousSignIn, Refresh }

    private sealed class RealtimeSubscription : IRealtimeSubscription
    {
        private readonly global::Supabase.Realtime.Interfaces.IRealtimeClient<RealtimeSocket, RealtimeChannel> _client;
        private readonly IReadOnlyList<RealtimeChannel> _channels;
        private int _isAlive = 1;

        public RealtimeSubscription(global::Supabase.Realtime.Interfaces.IRealtimeClient<RealtimeSocket, RealtimeChannel> client, IReadOnlyList<RealtimeChannel> channels)
        {
            _client = client;
            _channels = channels;
            _client.AddStateChangedHandler(OnStateChanged);
        }

        public bool IsAlive => Volatile.Read(ref _isAlive) == 1;
        public event EventHandler? Closed;

        private void OnStateChanged(
            global::Supabase.Realtime.Interfaces.IRealtimeClient<RealtimeSocket, RealtimeChannel> sender,
            global::Supabase.Realtime.Constants.SocketState state)
        {
            if (state is not (global::Supabase.Realtime.Constants.SocketState.Close or global::Supabase.Realtime.Constants.SocketState.Error)) return;
            if (Interlocked.Exchange(ref _isAlive, 0) == 1) Closed?.Invoke(this, EventArgs.Empty);
        }

        public async ValueTask DisposeAsync()
        {
            _client.RemoveStateChangedHandler(OnStateChanged);
            Interlocked.Exchange(ref _isAlive, 0);
            foreach (RealtimeChannel channel in _channels) channel.Unsubscribe();
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    [Table("timetables")] private sealed class RealtimeTimetable : BaseModel;
    [Table("periods")] private sealed class RealtimePeriod : BaseModel;
    [Table("classes")] private sealed class RealtimeClass : BaseModel;
    [Table("period_classes")] private sealed class RealtimePeriodClass : BaseModel;
    [Table("week_schedule")] private sealed class RealtimeWeekSchedule : BaseModel;
    [Table("date_overrides")] private sealed class RealtimeDateOverride : BaseModel;
    [Table("announcements")] private sealed class RealtimeAnnouncement : BaseModel;
    [Table("profiles")] private sealed class RealtimeProfile : BaseModel;
}
