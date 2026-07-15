using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace AqiClock.SupabaseTests;

public enum TestPersona
{
    Anon,
    Staff,
    Admin,
    Deactivated,
    CrossOrg,
}

/// <summary>
/// Provisions one disposable set of test identities against the running local stack:
/// two org-A admins, an org-A staff member, an org-A member deactivated *after* sign-in
/// (so their JWT is still formally valid), and a staff member moved to a second
/// organisation. Emails carry a per-run suffix so runs never collide; `supabase db reset`
/// clears everything.
/// </summary>
public sealed class SupabaseFixture : IAsyncLifetime, IDisposable
{
    public const string Password = "Aq1-test-password!";
    public static readonly Guid OrgAId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid SeedTimetableId = Guid.Parse("00000000-0000-0000-0000-000000000100");
    public static readonly Guid SeedWeekdayMondayId = Guid.Parse("00000000-0000-0000-0000-000000000200");
    public static readonly Guid SeedPeriodRegistrationId = Guid.Parse("00000000-0000-0000-0000-000000000301");
    public static readonly Guid OrgBFixedId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private readonly HttpClient _http = new();
    private readonly Func<string> _dbConnectionString = () =>
        NormalizeConnectionString(SupabaseEnvironment.DbConnectionString);
    private readonly Dictionary<TestPersona, string> _tokens = [];

    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];

    public Guid OrgBId { get; private set; }
    public Guid AdminUserId { get; private set; }
    public Guid SecondAdminUserId { get; private set; }
    public Guid StaffUserId { get; private set; }
    public Guid DeactivatedUserId { get; private set; }
    public Guid CrossOrgUserId { get; private set; }
    public Guid ProbeAnnouncementId { get; private set; }
    public Guid ProbeDateOverrideId { get; private set; }

    public async Task InitializeAsync()
    {
        if (!SupabaseEnvironment.IsConfigured)
        {
            return;
        }

        await EnsureCleanupAdminAsync();
        await DeletePersonaUsersAsync();

        OrgBId = OrgBFixedId;
        await SqlAsync(
            "insert into public.organizations (id, name) values ($1, $2) on conflict (id) do update set name = excluded.name",
            OrgBId, $"Test Org B {RunId}");

        AdminUserId = await CreateUserAsync(Email("admin1"));
        SecondAdminUserId = await CreateUserAsync(Email("admin2"));
        StaffUserId = await CreateUserAsync(Email("staff1"));
        DeactivatedUserId = await CreateUserAsync(Email("deact1"));
        CrossOrgUserId = await CreateUserAsync(Email("crossorg1"));

        await SqlAsync(
            "update public.profiles set role = 'admin' where id = $1 or id = $2",
            AdminUserId, SecondAdminUserId);
        await SqlAsync(
            "update public.profiles set org_id = $1 where id = $2",
            OrgBId, CrossOrgUserId);

        _tokens[TestPersona.Admin] = await SignInAsync(Email("admin1"));
        _tokens[TestPersona.Staff] = await SignInAsync(Email("staff1"));
        _tokens[TestPersona.CrossOrg] = await SignInAsync(Email("crossorg1"));
        _tokens[TestPersona.Deactivated] = await SignInAsync(Email("deact1"));

        // Deactivate AFTER sign-in: the matrix must prove a still-valid JWT is useless.
        await SqlAsync(
            "update public.profiles set is_active = false where id = $1",
            DeactivatedUserId);

        // Probe rows for tables the seed leaves empty.
        ProbeAnnouncementId = Guid.Parse("00000000-0000-0000-0000-000000000401");
        await SqlAsync(
            "insert into public.announcements (id, org_id, title, body, created_by) values ($1, $2, $3, $4, $5) on conflict (id) do update set title = excluded.title, body = excluded.body, created_by = excluded.created_by",
            ProbeAnnouncementId, OrgAId, "Probe announcement", "Probe body", AdminUserId);

        ProbeDateOverrideId = Guid.Parse("00000000-0000-0000-0000-000000000402");
        await SqlAsync(
            "insert into public.date_overrides (id, org_id, date, note) values ($1, $2, $3, $4) on conflict (id) do update set note = excluded.note",
            ProbeDateOverrideId, OrgAId, new DateOnly(2031, 1, 1), "Probe override");
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _http.Dispose();

    public static string Email(string role) => $"aqitest-{role}@example.invalid";

    // ---- REST (PostgREST) --------------------------------------------------

    public async Task<HttpResponseMessage> RestAsync(
        TestPersona persona,
        HttpMethod method,
        string pathAndQuery,
        JsonObject? body = null)
    {
        using var request = new HttpRequestMessage(method, $"{SupabaseEnvironment.Url}/rest/v1/{pathAndQuery}");
        string token = persona == TestPersona.Anon ? SupabaseEnvironment.AnonKey : _tokens[persona];
        request.Headers.Add("apikey", SupabaseEnvironment.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Prefer", "return=representation");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request);
    }

    /// <summary>Rows returned by a GET/PATCH/DELETE with return=representation; null when the request errored.</summary>
    public static async Task<JsonArray?> RowsAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text) as JsonArray;
    }

    // ---- GoTrue ------------------------------------------------------------

    public async Task<Guid> CreateUserAsync(string email, JsonObject? userMetadata = null)
    {
        var body = new JsonObject
        {
            ["email"] = email,
            ["password"] = Password,
            ["email_confirm"] = true,
        };
        if (userMetadata is not null)
        {
            body["user_metadata"] = userMetadata;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseEnvironment.Url}/auth/v1/admin/users")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("apikey", SupabaseEnvironment.ServiceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SupabaseEnvironment.ServiceRoleKey);

        HttpResponseMessage response = await _http.SendAsync(request);
        string payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Creating user {email} failed ({(int)response.StatusCode}): {payload}");
        }

        return Guid.Parse(JsonNode.Parse(payload)!["id"]!.GetValue<string>());
    }

    public Task<Guid> CreateUserWithMetadataAsync(string email, string displayName) =>
        CreateUserAsync(email, new JsonObject { ["display_name"] = displayName });

    private async Task DeleteUserAsync(Guid userId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{SupabaseEnvironment.Url}/auth/v1/admin/users/{userId}");
        request.Headers.Add("apikey", SupabaseEnvironment.ServiceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", SupabaseEnvironment.ServiceRoleKey);

        HttpResponseMessage response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string payload = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Deleting test user {userId} failed ({(int)response.StatusCode}): {payload}");
        }
    }

    private async Task EnsureCleanupAdminAsync()
    {
        const string cleanupEmail = "aqitest-cleanup-admin@example.invalid";
        Guid? cleanupId = await SqlScalarAsync<Guid?>(
            "select id from auth.users where email = $1 limit 1", cleanupEmail);
        if (cleanupId is null)
        {
            cleanupId = await CreateUserAsync(cleanupEmail);
        }

        await SqlAsync(
            "update public.profiles set org_id = $1, role = 'admin', is_active = true where id = $2",
            OrgAId, cleanupId.Value);
    }

    private async Task DeletePersonaUsersAsync()
    {
        string[] emails = [Email("admin1"), Email("admin2"), Email("staff1"), Email("deact1"), Email("crossorg1")];
        foreach (string email in emails)
        {
            Guid? userId = await SqlScalarAsync<Guid?>(
                "select id from auth.users where email = $1 limit 1", email);
            if (userId is not null)
            {
                await DeleteUserAsync(userId.Value);
            }
        }
    }

    private async Task<string> SignInAsync(string email)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SupabaseEnvironment.Url}/auth/v1/token?grant_type=password")
        {
            Content = JsonContent.Create(new JsonObject { ["email"] = email, ["password"] = Password }),
        };
        request.Headers.Add("apikey", SupabaseEnvironment.AnonKey);

        HttpResponseMessage response = await _http.SendAsync(request);
        string payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Sign-in for {email} failed ({(int)response.StatusCode}): {payload}");
        }

        return JsonNode.Parse(payload)!["access_token"]!.GetValue<string>();
    }

    // ---- Direct SQL (postgres superuser via local DB_URL) -------------------

    public async Task SqlAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(_dbConnectionString());
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> SqlScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(_dbConnectionString());
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, sql, parameters);
        object? result = await command.ExecuteScalarAsync();
        return result switch
        {
            null or DBNull => default,
            T typed => typed,
            Guid guid when Nullable.GetUnderlyingType(typeof(T)) == typeof(Guid) => (T)(object)guid,
            _ => (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture),
        };
    }

    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (object parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return command;
    }

    private static string NormalizeConnectionString(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != "postgresql" && uri.Scheme != "postgres"))
        {
            return value;
        }

        string[] userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length == 2 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            SslMode = SslMode.Disable,
        };
        return builder.ConnectionString;
    }
}

[CollectionDefinition("supabase")]
public sealed class SupabaseTestGroup : ICollectionFixture<SupabaseFixture>;
