using AqiClock.Domain.Time;
using AqiClock.Infrastructure.DependencyInjection;
using AqiClock.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqiClock.IntegrationTests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void InfrastructureRegistrationsBuildSuccessfully()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AqiClock:CacheFreshnessMinutes"] = "60",
                ["Supabase:Url"] = "https://example.supabase.co",
                ["Supabase:AnonKey"] = "test-only-key"
            })
            .Build();

        using ServiceProvider provider = new ServiceCollection()
            .AddAqiClockInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider);
        Assert.IsType<SystemClock>(provider.GetRequiredService<IClock>());
    }
}
