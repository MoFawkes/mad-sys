using System.ComponentModel.DataAnnotations;

namespace AqiClock.Application.Configuration;

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    [Required, Url]
    public string Url { get; init; } = string.Empty;

    [Required]
    public string AnonKey { get; init; } = string.Empty;
}
