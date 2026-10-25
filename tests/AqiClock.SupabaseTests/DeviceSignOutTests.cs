using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Infrastructure.Supabase;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AqiClock.SupabaseTests;

public sealed class DeviceSignOutTests
{
    [SupabaseFact]
    public async Task SigningOutRevokesOnlyTheCurrentDeviceSession()
    {
        var target = new Uri(SupabaseEnvironment.Url!);
        Assert.True(target.IsLoopback, "This disposable-identity test requires a local Supabase stack.");
        using var admin = new HttpClient { BaseAddress = new Uri(target, "/auth/v1/") };
        admin.DefaultRequestHeaders.Add("apikey", SupabaseEnvironment.ServiceRoleKey);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SupabaseEnvironment.ServiceRoleKey);
        string email = $"aqitest-signout-{Guid.NewGuid():N}@example.invalid";
        string password = $"Aq1-{Guid.NewGuid():N}!";
        using HttpResponseMessage created = await admin.PostAsJsonAsync("admin/users", new { email, password, email_confirm = true });
        created.EnsureSuccessStatusCode();
        using JsonDocument identity = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid userId = identity.RootElement.GetProperty("id").GetGuid();
        try
        {
            using SupabaseGateway firstDevice = CreateGateway();
            using SupabaseGateway secondDevice = CreateGateway();
            AuthenticatedSession first = await firstDevice.SignInAsync(email, password);
            AuthenticatedSession second = await secondDevice.SignInAsync(email, password);

            await firstDevice.SignOutAsync();

            await Assert.ThrowsAsync<AuthenticationRejectedException>(() => firstDevice.RefreshSessionAsync(
                new StoredSession(first.AccessToken, first.RefreshToken, first.ExpiresAt)));
            AuthenticatedSession refreshed = await secondDevice.RefreshSessionAsync(
                new StoredSession(second.AccessToken, second.RefreshToken, second.ExpiresAt));
            Assert.Equal(userId, refreshed.UserId);
        }
        finally
        {
            using HttpResponseMessage deleted = await admin.DeleteAsync($"admin/users/{userId}");
            deleted.EnsureSuccessStatusCode();
        }
    }

    private static SupabaseGateway CreateGateway() => new(Options.Create(new SupabaseOptions
    {
        Url = SupabaseEnvironment.Url!,
        AnonKey = SupabaseEnvironment.AnonKey,
    }), NullLogger<SupabaseGateway>.Instance);
}
