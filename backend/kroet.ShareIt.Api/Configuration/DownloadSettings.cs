using FluentValidation;

namespace kroet.ShareIt.Api.Configuration;

/// <summary>
/// Configuration settings for access sessions and download limits.
/// </summary>
public sealed class DownloadSettings : ISettings
{
    /// <summary>
    /// How long an access session authorizes package reads and downloads after
    /// a successful open. Fixed: sessions are not renewed automatically.
    /// Default: 15 minutes.
    /// </summary>
    public int AccessSessionLifetimeMinutes { get; set; } = 15;

    /// <summary>
    /// How long an access-limited package remains available after its most
    /// recent access, so downloads from that access can still complete. Must be
    /// at least <see cref="AccessSessionLifetimeMinutes"/>.
    /// Default: 30 minutes.
    /// </summary>
    public int ExhaustedPackageRetentionMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum size of a single item (in bytes) that GET /package/{id} may
    /// read fully into memory and inline as a base64url payload. Larger items
    /// are served through the access session instead.
    /// Default: 16 KB.
    /// </summary>
    public long InlinePayloadLimitBytes { get; set; } = 16 * 1024;

    /// <summary>
    /// Total inline payload budget per response (in bytes). Once the budget is
    /// spent, every remaining item is served through the access session, so a
    /// package full of small notes cannot turn the details response into a
    /// megabyte of base64.
    /// Default: 64 KB.
    /// </summary>
    public long InlinePayloadBudgetBytes { get; set; } = 64 * 1024;

    public static string SectionName => "DownloadSettings";
}

public sealed class DownloadSettingsValidator : AbstractValidator<DownloadSettings>
{
    public DownloadSettingsValidator()
    {
        RuleFor(x => x.AccessSessionLifetimeMinutes).GreaterThan(0);

        RuleFor(x => x.ExhaustedPackageRetentionMinutes)
            .GreaterThanOrEqualTo(x => x.AccessSessionLifetimeMinutes)
            .WithMessage(
                "ExhaustedPackageRetentionMinutes must be at least AccessSessionLifetimeMinutes " +
                "so the final access remains usable for downloads.");

        RuleFor(x => x.InlinePayloadLimitBytes).GreaterThanOrEqualTo(0);

        RuleFor(x => x.InlinePayloadBudgetBytes).GreaterThanOrEqualTo(0);
    }
}