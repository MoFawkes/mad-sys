namespace AqiClock.SupabaseTests;

/// <summary>
/// These tests need a running local Supabase stack (`npx supabase start`). They skip
/// cleanly when SUPABASE_URL is not set, so `dotnet test AqiClock.sln` stays green on
/// machines and CI jobs without Docker.
/// </summary>
public sealed class SupabaseFactAttribute : FactAttribute
{
    public SupabaseFactAttribute()
    {
        if (!SupabaseEnvironment.IsConfigured)
        {
            Skip = "SUPABASE_URL is not set; start the local stack and export supabase status env vars.";
        }
    }
}

public sealed class SupabaseTheoryAttribute : TheoryAttribute
{
    public SupabaseTheoryAttribute()
    {
        if (!SupabaseEnvironment.IsConfigured)
        {
            Skip = "SUPABASE_URL is not set; start the local stack and export supabase status env vars.";
        }
    }
}

public static class SupabaseEnvironment
{
    public static string? Url => Environment.GetEnvironmentVariable("SUPABASE_URL");

    public static string AnonKey =>
        Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
        ?? throw new InvalidOperationException("SUPABASE_ANON_KEY is not set.");

    public static string ServiceRoleKey =>
        Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")
        ?? throw new InvalidOperationException("SUPABASE_SERVICE_ROLE_KEY is not set.");

    public static string DbConnectionString =>
        Environment.GetEnvironmentVariable("SUPABASE_DB_URL")
        ?? throw new InvalidOperationException("SUPABASE_DB_URL is not set.");

    public static bool IsConfigured => !string.IsNullOrEmpty(Url);
}
