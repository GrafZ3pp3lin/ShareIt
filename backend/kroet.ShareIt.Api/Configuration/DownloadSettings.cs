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
    }
}
