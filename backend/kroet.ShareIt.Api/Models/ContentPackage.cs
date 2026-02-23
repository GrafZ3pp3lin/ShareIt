namespace kroet.ShareIt.Api.Models;

/// <summary>
/// Represents a package of content items (files and/or text) that can be shared together.
/// </summary>
public record ContentPackage
{
    /// <summary>
    /// Unique identifier for the package.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Optional title/name for the package.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Optional description of the package.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Timestamp when the package was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Optional identifier of the user who created the package.
    /// </summary>
    public Guid? UserId { get; init; }
    
    /// <summary>
    /// Whether the package is private (owner-only access).
    /// </summary>
    public bool? IsPrivate { get; init; }

    /// <summary>
    /// List of contents in this package.
    /// </summary>
    public List<ExternalContent>? Contents { get; set; }

    /// <summary>
    /// Maximum number of times this package can be accessed.
    /// Null means unlimited access.
    /// </summary>
    public int? MaxDownloadCount { get; init; }

    /// <summary>
    /// Current number of times this package has been accessed.
    /// </summary>
    public int CurrentDownloadCount { get; init; }

    /// <summary>
    /// Optional expiration timestamp for the package.
    /// Null means no expiration.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Gets the remaining number of accesses available.
    /// Null if unlimited.
    /// </summary>
    public int? RemainingDownloads => MaxDownloadCount.HasValue 
        ? Math.Max(0, MaxDownloadCount.Value - CurrentDownloadCount) 
        : null;

    /// <summary>
    /// Whether the access limit has been reached.
    /// </summary>
    public bool IsAccessLimitReached => MaxDownloadCount.HasValue && CurrentDownloadCount >= MaxDownloadCount.Value;

    /// <summary>
    /// Whether the package has expired.
    /// </summary>
    public bool IsExpired(DateTimeOffset currentTime) => ExpiresAt.HasValue && currentTime >= ExpiresAt.Value;
}

