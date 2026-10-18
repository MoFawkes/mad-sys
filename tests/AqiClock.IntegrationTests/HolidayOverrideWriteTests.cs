using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Infrastructure.Supabase;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AqiClock.IntegrationTests;

public sealed class HolidayOverrideWriteTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.OK, true)]
    public async Task HolidayWeekUsesOneAtomicUpsertWithExplicitNullsAndNoReplacementIds(HttpStatusCode responseStatus, bool shortResponse)
    {
        using var gateway = new SupabaseGateway(
            Options.Create(new SupabaseOptions { Url = "http://127.0.0.1:1", AnonKey = "test-key" }),
            NullLogger<SupabaseGateway>.Instance);
        using var handler = new CaptureHandler { ResponseStatus = responseStatus, ShortResponse = shortResponse };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1") };
        FieldInfo field = typeof(SupabaseGateway).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(gateway)!).Dispose();
        field.SetValue(gateway, client);
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"sub\":\"{Guid.NewGuid()}\"}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await gateway.RestoreAccessTokenAsync(new StoredSession($"e30.{payload}.signature", "unused", DateTimeOffset.UtcNow.AddHours(1)));

        if (responseStatus != HttpStatusCode.OK || shortResponse)
            await Assert.ThrowsAnyAsync<ServerWriteException>(() => gateway.CloseDateRangeAsync(new(2026, 10, 19), new(2026, 10, 25), " Half term "));
        else
            Assert.Equal(7, await gateway.CloseDateRangeAsync(new(2026, 10, 19), new(2026, 10, 25), " Half term "));
        Assert.Equal(1, handler.WriteCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/rest/v1/date_overrides?on_conflict=org_id,date", handler.Path);
        Assert.Contains("resolution=merge-duplicates", handler.Prefer);
        Assert.Contains("return=representation", handler.Prefer);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(7, body.RootElement.GetArrayLength());
        for (int offset = 0; offset < 7; offset++)
        {
            JsonElement row = body.RootElement[offset];
            Assert.Equal(new DateOnly(2026, 10, 19).AddDays(offset).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), row.GetProperty("date").GetString());
            Assert.Equal(handler.OrgId, row.GetProperty("org_id").GetGuid());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("timetable_id").ValueKind);
            Assert.Equal("Half term", row.GetProperty("note").GetString());
            Assert.False(row.TryGetProperty("id", out _));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavingExistingOverrideSendsClosedOrAssignedTimetableExplicitly(bool closed)
    {
        using var gateway = new SupabaseGateway(
            Options.Create(new SupabaseOptions { Url = "http://127.0.0.1:1", AnonKey = "test-key" }),
            NullLogger<SupabaseGateway>.Instance);
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1") };
        // Exercise the gateway's real request serialization without a hosted database.
        FieldInfo field = typeof(SupabaseGateway).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(gateway)!).Dispose();
        field.SetValue(gateway, client);
        Guid id = Guid.NewGuid();
        Guid? timetableId = closed ? null : Guid.NewGuid();

        await gateway.UpdateAsync(CacheTable.DateOverrides, id,
            new DateOverrideRow(id, Guid.NewGuid(), new DateOnly(2026, 10, 19), timetableId, "Holiday"));

        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.Equal($"/rest/v1/date_overrides?id=eq.{id}", handler.Path);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.True(body.RootElement.TryGetProperty("timetable_id", out JsonElement value),
            "Closed must send timetable_id:null; omitting the column leaves the previous timetable unchanged.");
        if (closed) Assert.Equal(JsonValueKind.Null, value.ValueKind);
        else Assert.Equal(timetableId, value.GetGuid());
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public string Prefer { get; private set; } = string.Empty;
        public Guid OrgId { get; } = Guid.NewGuid();
        public int WriteCount { get; private set; }
        public HttpStatusCode ResponseStatus { get; init; } = HttpStatusCode.OK;
        public bool ShortResponse { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"[{{\"org_id\":\"{OrgId}\"}}]", Encoding.UTF8, "application/json") };
            WriteCount++;
            Method = request.Method;
            Path = request.RequestUri!.PathAndQuery;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Prefer = string.Join(",", request.Headers.GetValues("Prefer"));
            if (request.Method == HttpMethod.Post)
                return new HttpResponseMessage(ResponseStatus) { Content = new StringContent(ShortResponse ? "[]" : Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
