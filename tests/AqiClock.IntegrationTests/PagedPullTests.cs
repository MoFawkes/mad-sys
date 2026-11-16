using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Infrastructure.Supabase;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AqiClock.IntegrationTests;

public sealed class PagedPullTests
{
    [Theory]
    [InlineData(3, 1000, true, 1)]
    // More rows than the server returns per response: every row must still reach the cache.
    [InlineData(2345, 1000, true, 3)]
    // A hosted cap below the client page size must not end the read early.
    [InlineData(1200, 500, true, 3)]
    // Without a count header the pull reads until an empty page.
    [InlineData(1500, 1000, false, 3)]
    [InlineData(0, 1000, true, 1)]
    public async Task PullReadsEveryRowBeyondTheServerRowCap(int total, int serverCap, bool reportCount, int expectedRequests)
    {
        using var handler = new PagingHandler(total, serverCap, reportCount);
        using SupabaseGateway gateway = CreateGateway(handler);

        CacheSnapshot snapshot = await gateway.PullAsync(CacheTable.Timetables);

        Assert.Equal(total, snapshot.Rows.Count);
        Assert.Equal(total, snapshot.Rows.Cast<TimetableRow>().Select(row => row.Id).Distinct().Count());
        Assert.Equal(expectedRequests, handler.Queries.Count);
        Assert.All(handler.Queries, query =>
        {
            Assert.Equal("id.asc", query["order"]);
            Assert.Equal(SupabaseGateway.PullPageSize.ToString(CultureInfo.InvariantCulture), query["limit"]);
        });
        Assert.All(handler.Prefer, prefer => Assert.Contains("count=exact", prefer));
    }

    [Fact]
    public async Task LinkTableIsOrderedByItsCompositeKey()
    {
        using var handler = new PagingHandler(0, 1000, true);
        using SupabaseGateway gateway = CreateGateway(handler);

        await gateway.PullAsync(CacheTable.PeriodClasses);

        Assert.Equal("period_id.asc,class_id.asc", Assert.Single(handler.Queries)["order"]);
    }

    private static SupabaseGateway CreateGateway(HttpMessageHandler handler)
    {
        var gateway = new SupabaseGateway(
            Options.Create(new SupabaseOptions { Url = "http://127.0.0.1:1", AnonKey = "test-key" }),
            NullLogger<SupabaseGateway>.Instance);
        // Exercise the gateway's real request building without a hosted database.
        FieldInfo field = typeof(SupabaseGateway).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(gateway)!).Dispose();
        field.SetValue(gateway, new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1") });
        return gateway;
    }

    // Mimics PostgREST: an ordered offset read capped at serverCap rows per response.
    private sealed class PagingHandler(int total, int serverCap, bool reportCount) : HttpMessageHandler
    {
        public List<System.Collections.Specialized.NameValueCollection> Queries { get; } = [];
        public List<string> Prefer { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            Queries.Add(query);
            Prefer.Add(string.Join(",", request.Headers.GetValues("Prefer")));
            int offset = int.Parse(query["offset"]!, CultureInfo.InvariantCulture);
            int limit = int.Parse(query["limit"]!, CultureInfo.InvariantCulture);
            int count = Math.Max(0, Math.Min(total, offset + Math.Min(limit, serverCap)) - offset);
            var body = new StringBuilder("[");
            for (int index = 0; index < count; index++)
            {
                if (index > 0) body.Append(',');
                body.Append(CultureInfo.InvariantCulture,
                    $"{{\"id\":\"{new Guid(offset + index, 0, 0, new byte[8])}\",\"org_id\":\"{Guid.Empty}\",\"name\":\"T\",\"is_archived\":false}}");
            }
            body.Append(']');
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
            };
            if (reportCount)
            {
                string range = count == 0 ? "*" : $"{offset}-{offset + count - 1}";
                response.Content.Headers.TryAddWithoutValidation("Content-Range", $"{range}/{total}");
            }
            return Task.FromResult(response);
        }
    }
}
