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
        MaxDownloadCount = 1
    };

    /// <summary>
    /// Limits that apply to authenticated uploads.
    /// </summary>
    public UploadIdentitySettings Authenticated { get; set; } = new()
    {
        FileLimitMb = 100,
        MaxLifetimeHours = 24 * 30,
        MaxDownloadCount = 10
    };

    /// <summary>
    /// Maximum total size of a single HTTP request body (all files in one upload),
    /// in megabytes. Applies to every caller.
    /// Default: 512 MB.
    /// </summary>
    public int MaxRequestBodySizeMb { get; set; } = 512;

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
    public int MaxDownloadCount { get; set; }
}

public sealed class UploadSettingsValidator : AbstractValidator<UploadSettings>
{
    public UploadSettingsValidator()
    {
        RuleFor(x => x.Anonymous.FileLimitMb).GreaterThan(0);

        RuleFor(x => x.Authenticated.FileLimitMb).GreaterThanOrEqualTo(x => x.Anonymous.FileLimitMb);

        RuleFor(x => x.MaxRequestBodySizeMb).GreaterThanOrEqualTo(x => x.Authenticated.FileLimitMb);

        RuleFor(x => x.Anonymous.MaxLifetimeHours).GreaterThan(0);

        RuleFor(x => x.Authenticated.MaxLifetimeHours).GreaterThanOrEqualTo(x => x.Anonymous.MaxLifetimeHours);

        RuleFor(x => x.Anonymous.MaxDownloadCount).GreaterThan(0);

        RuleFor(x => x.Authenticated.MaxDownloadCount).GreaterThanOrEqualTo(x => x.Anonymous.MaxDownloadCount);
    }
}
