namespace AqiClock.Domain.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void DomainAssemblyIsAvailable() => Assert.NotNull(typeof(AssemblyMarker).Assembly);
}
