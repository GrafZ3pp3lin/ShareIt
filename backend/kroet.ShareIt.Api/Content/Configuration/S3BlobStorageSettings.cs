using FluentValidation;

namespace kroet.ShareIt.Api.Content.Configuration;

/// <summary>
/// Configuration settings for AWS S3 Blob Storage.
/// </summary>
public class S3BlobStorageSettings : ISettings
{
    /// <summary>
    /// AWS Access Key ID for authentication.
    /// </summary>
    public required string AccessKeyId { get; init; }
    
    /// <summary>
    /// AWS Secret Access Key for authentication.
    /// </summary>
    public required string SecretAccessKey { get; init; }
    
    /// <summary>
    /// Service URL for S3-compatible storage (e.g., MinIO, Wasabi).
    /// </summary>
    public required string ServiceUrl { get; init; }
    
    /// <summary>
    /// Name of the S3 bucket to store files.
    /// Default: "content-files"
    /// </summary>
    public string BucketName { get; init; } = "content-files";

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

