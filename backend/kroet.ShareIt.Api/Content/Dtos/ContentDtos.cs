using kroet.ShareIt.Api.Content.Models;

namespace kroet.ShareIt.Api.Content.Dtos;

/// <summary>
/// Request to create a new content package.
/// </summary>
public class CreatePackageRequest
{
    /// <summary>
    /// Optional title/name for the package.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Optional description of the package.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Maximum number of times this package can be accessed.
    /// Null means unlimited.
    /// </summary>
    public int? MaxDownloadCount { get; set; }

    /// <summary>
    /// Time to live in hours. Package will expire after this duration.
    /// Null means no expiration.
    /// </summary>
    public int? TimeToLiveHours { get; set; }
    
    /// <summary>
    /// Whether the package is private (owner-only access).
    /// </summary>
    public bool? IsPrivate { get; set;  }

    /// <summary>
    /// Optional text content to include in the package.
    /// </summary>
    public string[]? Texts { get; set; }

    /// <summary>
    /// Optional files to include in the package.
    /// </summary>
    public IFormFileCollection? Files { get; set; }
}

/// <summary>
/// Request DTO for creating text content.
/// </summary>
public class CreateTextRequest
{
    /// <summary>
    /// The plain text content.
    /// </summary>
    public required string Text { get; set; }
}

/// <summary>
/// Response for a content package.
/// </summary>
public class ContentPackageResponse
{
    /// <summary>
    /// Package ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Package title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Package description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// When the package was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// User who created the package.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Number of items in the package.
    /// </summary>
    public required int ItemCount { get; init; }

    /// <summary>
    /// Maximum access count.
    /// </summary>
    public int? MaxDownloadCount { get; init; }

    /// <summary>
    /// Remaining accesses.
    /// </summary>
    public int? RemainingDownloads { get; init; }

    /// <summary>
    /// Optional expiration timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public static ContentPackageResponse FromPackage(ContentPackage package)
    {
        return new ContentPackageResponse
        {
            Id = package.Id,
            Title = package.Title,
            Description = package.Description,
            CreatedAt = package.CreatedAt,
            UserId = package.UserId,
            ItemCount = package.Contents?.Count ?? 0,
            MaxDownloadCount = package.MaxDownloadCount,
            RemainingDownloads = package.RemainingDownloads,
            ExpiresAt = package.ExpiresAt,
        };
    }
}

/// <summary>
/// Detailed response for a package with all its content items.
/// </summary>
public class ContentPackageDetailsResponse
{
    /// <summary>
    /// Package metadata.
    /// </summary>
    public required ContentPackageResponse Package { get; init; }

    /// <summary>
    /// List of content items in the package.
    /// </summary>
    public required List<ContentItemInPackage> Items { get; init; }
}

/// <summary>
/// Represents a content item within a package.
/// </summary>
public class ContentItemInPackage
{
    /// <summary>
    /// Content ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Content type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// File name (for files).
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// MIME type.
    /// </summary>
    public required string MimeType { get; init; }

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// Text content (for text items).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Download token (for files).
    /// </summary>
    public string? DownloadToken { get; init; }

    /// <summary>
    /// Download URL (for files).
    /// </summary>
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// Token expiration (for files).
    /// </summary>
    public DateTimeOffset? TokenExpiresAt { get; init; }
}

