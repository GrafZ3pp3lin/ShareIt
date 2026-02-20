using FluentValidation;

namespace kroet.ShareIt.Api.Content.Configuration;

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
    /// Whether to validate client IP when consuming tokens.
    /// If true, tokens can only be used from the same IP that requested them.
    /// Default: false.
    /// </summary>
    public bool ValidateClientIp { get; set; } = false;

    /// <summary>
    /// How long to keep used tokens before cleanup (for audit purposes) in hours.
    /// Default: 24 hours.
    /// </summary>
    public int UsedTokenRetentionHours { get; set; } = 24;

    public static string SectionName => "DownloadSettings";
}

public sealed class DownloadSettingsValidator : AbstractValidator<DownloadSettings>
{
    public DownloadSettingsValidator()
    {
        RuleFor(x => x.DefaultTokenExpirationMinutes).GreaterThan(0);

        RuleFor(x => x.UsedTokenRetentionHours).GreaterThanOrEqualTo(0);
    }
}