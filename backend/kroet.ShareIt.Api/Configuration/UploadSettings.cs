using FluentValidation;

namespace kroet.ShareIt.Api.Configuration;

/// <summary>
/// Configuration settings for upload limits: per-identity file size and
/// package lifetime caps.
/// </summary>
public sealed class UploadSettings : ISettings
{
    /// <summary>
    /// Limits that apply to anonymous (guest) uploads.
    /// </summary>
    public UploadIdentitySettings Anonymous { get; set; } = new()
    {
        FileLimitMb = 25,
        MaxLifetimeHours = 6,
        MaxAccessCount = 1
    };

    /// <summary>
    /// Limits that apply to authenticated uploads.
    /// </summary>
    public UploadIdentitySettings Authenticated { get; set; } = new()
    {
        FileLimitMb = 100,
        MaxLifetimeHours = 24 * 30,
        MaxAccessCount = 10
    };

    /// <summary>
    /// Maximum size of a single raw upload request body, in megabytes. Uploads
    /// carry one item per request. Applies to every caller.
    /// Default: 512 MB.
    /// </summary>
    public int MaxRequestBodySizeMb { get; set; } = 512;

    /// <summary>
    /// Maximum number of items (text and files combined) a single package may
    /// contain. Applies to every caller.
    /// Default: 100.
    /// </summary>
    public int MaxItemsPerPackage { get; set; } = 100;

    public static string SectionName => "UploadSettings";
}

/// <summary>
/// Per-identity upload limits.
/// </summary>
public sealed class UploadIdentitySettings
{
    /// <summary>
    /// Maximum size of a single uploaded file, in megabytes.
    /// </summary>
    public int FileLimitMb { get; set; }

    /// <summary>
    /// Maximum package lifetime, in hours.
    /// </summary>
    public int MaxLifetimeHours { get; set; }

    /// <summary>
    /// Maximum number of times a package may be accessed.
    /// </summary>
    public int MaxAccessCount { get; set; }
}

public sealed class UploadSettingsValidator : AbstractValidator<UploadSettings>
{
    public UploadSettingsValidator()
    {
        RuleFor(x => x.Anonymous.FileLimitMb).GreaterThan(0);

        RuleFor(x => x.Authenticated.FileLimitMb).GreaterThanOrEqualTo(x => x.Anonymous.FileLimitMb);

        RuleFor(x => x.MaxRequestBodySizeMb).GreaterThanOrEqualTo(x => x.Authenticated.FileLimitMb);

        RuleFor(x => x.MaxItemsPerPackage).GreaterThan(0);

        RuleFor(x => x.Anonymous.MaxLifetimeHours).GreaterThan(0);

        RuleFor(x => x.Authenticated.MaxLifetimeHours).GreaterThanOrEqualTo(x => x.Anonymous.MaxLifetimeHours);

        RuleFor(x => x.Anonymous.MaxAccessCount).GreaterThan(0);

        RuleFor(x => x.Authenticated.MaxAccessCount).GreaterThanOrEqualTo(x => x.Anonymous.MaxAccessCount);
    }
}
