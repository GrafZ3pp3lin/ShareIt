using FluentValidation;

namespace kroet.ShareIt.Api.Configuration;

/// <summary>
/// Configuration settings for upload size limits.
/// </summary>
public sealed class UploadSettings : ISettings
{
    /// <summary>
    /// Maximum size of a single uploaded file for anonymous (guest) users, in megabytes.
    /// Default: 25 MB.
    /// </summary>
    public int AnonymousFileLimitMb { get; set; } = 25;

    /// <summary>
    /// Maximum size of a single uploaded file for authenticated users, in megabytes.
    /// Default: 100 MB.
    /// </summary>
    public int AuthenticatedFileLimitMb { get; set; } = 100;

    /// <summary>
    /// Maximum total size of a single HTTP request body (all files in one upload),
    /// in megabytes. Applies to every caller.
    /// Default: 512 MB.
    /// </summary>
    public int MaxRequestBodySizeMb { get; set; } = 512;

    public static string SectionName => "UploadSettings";
}

public sealed class UploadSettingsValidator : AbstractValidator<UploadSettings>
{
    public UploadSettingsValidator()
    {
        RuleFor(x => x.AnonymousFileLimitMb).GreaterThan(0);

        RuleFor(x => x.AuthenticatedFileLimitMb).GreaterThanOrEqualTo(x => x.AnonymousFileLimitMb);

        RuleFor(x => x.MaxRequestBodySizeMb).GreaterThanOrEqualTo(x => x.AuthenticatedFileLimitMb);
    }
}
