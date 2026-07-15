using AqiClock.Application.Configuration;
using System.ComponentModel.DataAnnotations;

namespace AqiClock.Application.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void SupabaseOptionsRejectMissingValues()
    {
        var options = new SupabaseOptions();
        var results = new List<ValidationResult>();

        bool valid = Validator.TryValidateObject(options, new ValidationContext(options), results, true);

        Assert.False(valid);
        Assert.NotEmpty(results);
    }
}
