using FluentValidation;
using kroet.ShareIt.Api.Configuration;

namespace kroet.ShareIt.Api.Storage.S3;

/// <summary>
/// Configuration settings for AWS S3 Blob Storage.
/// </summary>
/// <remarks>
/// Properties use setters (not init-only setters): the source-generated
/// configuration binder does not populate init-only properties.
/// </remarks>
public class S3BlobStorageSettings : ISettings
{
    /// <summary>
    /// AWS Access Key ID for authentication.
    /// </summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>
    /// AWS Secret Access Key for authentication.
    /// </summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Service URL for S3-compatible storage (e.g., MinIO, Wasabi).
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Name of the S3 bucket to store files.
    /// Default: "content-files"
    /// </summary>
    public string BucketName { get; set; } = "content-files";

    public static string SectionName => "BlobStorage:S3";
}

public sealed class S3BlobStorageSettingsValidator : AbstractValidator<S3BlobStorageSettings>
{
    public S3BlobStorageSettingsValidator()
    {
        RuleFor(x => x.AccessKeyId).NotEmpty();
        RuleFor(x => x.SecretAccessKey).NotEmpty();
        RuleFor(x => x.ServiceUrl).NotEmpty();
        RuleFor(x => x.BucketName).NotEmpty();
    }
}

