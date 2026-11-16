using FluentValidation;

namespace kroet.ShareIt.Api.Configuration;

/// <summary>
/// Configuration settings for download tokens and limits.
/// </summary>
public sealed class DownloadSettings : ISettings
{
    /// <summary>
    /// Default expiration time for download tokens in minutes.
    /// Default: 15 minutes.
    /// </summary>
    public int DefaultTokenExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// How long to keep used tokens before cleanup (for audit purposes) in hours.
    /// Default: 24 hours.
    /// </summary>
    public int UsedTokenRetentionHours { get; set; } = 24;

    /// <summary>
    /// How long an access-limited package remains available after its most
    /// recent access, so tokens issued by that access can still be downloaded.
    /// Must be at least <see cref="DefaultTokenExpirationMinutes"/>.
    /// Default: 30 minutes.
    /// </summary>
    public int ExhaustedPackageRetentionMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum size of a single item (in bytes) that GET /package/{id} may
    /// read fully into memory and inline as a base64url payload. Larger items
    /// are served through a download token instead.
    /// Default: 16 KB.
    /// </summary>
    public long InlinePayloadLimitBytes { get; set; } = 16 * 1024;

    /// <summary>
    /// Total inline payload budget per response (in bytes). Once the budget is
    /// spent, every remaining item is served through a download token, so a
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
        RuleFor(x => x.DefaultTokenExpirationMinutes).GreaterThan(0);

        RuleFor(x => x.UsedTokenRetentionHours).GreaterThanOrEqualTo(0);

        RuleFor(x => x.ExhaustedPackageRetentionMinutes)
            .GreaterThanOrEqualTo(x => x.DefaultTokenExpirationMinutes)
            .WithMessage(
                "ExhaustedPackageRetentionMinutes must be at least DefaultTokenExpirationMinutes " +
                "so tokens issued by the final access remain usable.");

        RuleFor(x => x.InlinePayloadLimitBytes).GreaterThanOrEqualTo(0);

        RuleFor(x => x.InlinePayloadBudgetBytes).GreaterThanOrEqualTo(0);
    }
}