using System.Net;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class SignupGateTests(SupabaseFixture fixture)
{
    [SupabaseFact]
    public async Task PublicEmailSignupIsRejectedByHook()
    {
        string email = $"aqitest-public-signup-{fixture.RunId}@example.invalid";
        using HttpResponseMessage response = await fixture.PublicEmailSignupAsync(email);
        string payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Public signup is disabled", payload, StringComparison.Ordinal);
        Assert.Equal(
            0L,
            await fixture.SqlScalarAsync<long>(
                "select count(*) from auth.users where email = $1",
                email));
    }

    [SupabaseFact]
    public void AnonymousSignInSucceeds()
    {
        Assert.NotEqual(Guid.Empty, fixture.StudentDeviceUserId);
        Assert.NotEqual(Guid.Empty, fixture.UnenrolledStudentUserId);
    }

    [SupabaseFact]
    public void ServiceRoleUserCreationStillSucceeds()
    {
        Assert.NotEqual(Guid.Empty, fixture.AdminUserId);
        Assert.NotEqual(Guid.Empty, fixture.StaffUserId);
    }

    [SupabaseFact]
    public async Task ExistingTeacherPasswordSignInStillSucceeds()
    {
        using HttpResponseMessage response = await fixture.RestAsync(
            TestPersona.Staff,
            HttpMethod.Get,
            "profiles?select=id&id=eq." + fixture.StaffUserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single((await SupabaseFixture.RowsAsync(response))!);
    }
}
