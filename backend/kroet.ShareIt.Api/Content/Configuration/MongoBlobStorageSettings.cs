using FluentValidation;

namespace kroet.ShareIt.Api.Content.Configuration;

/// <summary>
/// Configuration settings for MongoDB Document-Based Blob Storage.
/// </summary>
public sealed class MongoBlobStorageSettings : ISettings
{
    /// <summary>
    /// MongoDB connection string (e.g., "mongodb://localhost:27017").
    /// </summary>
    public required string ConnectionString { get; init; }
    
    /// <summary>
    /// Name of the database to use.
    /// </summary>
    public required string DatabaseName { get; init; }
    
    /// <summary>
    /// Collection name for storing file documents.
    /// Default: "content-files"
    /// </summary>
    public string BucketName { get; init; } = "content-files";

    public static string SectionName => "BlobStorage:MongoDb";
}

public sealed class MongoBlobStorageSettingsValidator : AbstractValidator<MongoBlobStorageSettings>
{
    public MongoBlobStorageSettingsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.DatabaseName).NotEmpty();
        RuleFor(x => x.BucketName).NotEmpty();
    }
}

