using FluentValidation;

namespace kroet.ShareIt.Api.Configuration;

/// <summary>
/// Rate limits for reading a package link and downloading its content. Package
/// reads are partitioned on (package id, remote IP); downloads are partitioned
/// on remote IP only, which bounds limiter cardinality.
/// </summary>
public sealed class RateLimitSettings : ISettings
{
    /// <summary>
    /// Requests allowed per (package id, remote IP) window.
    /// Default: 20.
    /// </summary>
    public int PackageReadPermitLimit { get; set; } = 20;

    /// <summary>
    /// Length of the sliding window in minutes.
    /// Default: 15.
    /// </summary>
    public int PackageReadWindowMinutes { get; set; } = 15;

    /// <summary>
    /// Download requests allowed per remote IP window. A valid session can
    /// download items repeatedly, so this bounds bandwidth abuse.
    /// Default: 120.
    /// </summary>
    public int DownloadPermitLimit { get; set; } = 120;

    /// <summary>
    /// Length of the download rate-limit window in minutes.
    /// Default: 1.
    /// </summary>
    public int DownloadWindowMinutes { get; set; } = 1;

    /// <summary>
    /// Upload requests allowed per remote IP window. Uploads are one item per
    /// request, so this bounds bulk-upload abuse without throttling normal
    /// use.
    /// Default: 120.
    /// </summary>
    public int UploadPermitLimit { get; set; } = 120;

    /// <summary>
    /// Length of the upload rate-limit window in minutes.
    /// Default: 1.
    /// </summary>
    public int UploadWindowMinutes { get; set; } = 1;

    public static string SectionName => "RateLimitSettings";
}

/// <summary>
/// Names of the rate-limit policies attached to endpoints.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Limits reads of a package link, partitioned on (package id, remote IP).
    /// </summary>
    public const string PackageRead = "package-read";

    /// <summary>
    /// Limits downloads, partitioned on remote IP.
    /// </summary>
    public const string Download = "download";

    /// <summary>
    /// Limits uploads, partitioned on remote IP.
    /// </summary>
    public const string Upload = "upload";
}

public sealed class RateLimitSettingsValidator : AbstractValidator<RateLimitSettings>
{
    public RateLimitSettingsValidator()
    {
        RuleFor(x => x.PackageReadPermitLimit).GreaterThan(0);

        RuleFor(x => x.PackageReadWindowMinutes).GreaterThan(0);

        RuleFor(x => x.DownloadPermitLimit).GreaterThan(0);

        RuleFor(x => x.DownloadWindowMinutes).GreaterThan(0);

        RuleFor(x => x.UploadPermitLimit).GreaterThan(0);

        RuleFor(x => x.UploadWindowMinutes).GreaterThan(0);
    }
}
