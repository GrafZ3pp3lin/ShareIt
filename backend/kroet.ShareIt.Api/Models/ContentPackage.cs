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
    /// Base64url HMAC-SHA256 proof of the package passphrase, or null when
    /// the package is not protected. The server only compares it in constant
    /// time and never logs it.
    /// </summary>
    public string? UnlockVerifier { get; init; }

    /// <summary>
    /// Timestamp when the package was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Optional identifier of the user who created the package.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// List of contents in this package.
    /// </summary>
    public List<Content>? Contents { get; set; }

    /// <summary>
    /// Maximum number of times this package can be accessed.
    /// Null means unlimited access.
    /// </summary>
    public int? MaxAccessCount { get; init; }

    /// <summary>
    /// Current number of times this package has been accessed.
    /// </summary>
    public int CurrentAccessCount { get; init; }

    /// <summary>
    /// Timestamp of the most recent successful access claim. Cleanup uses this
    /// to keep an exhausted package available to its outstanding downloads.
    /// </summary>
    public DateTimeOffset? LastAccessAt { get; init; }

    /// <summary>
    /// Expiration timestamp. Every package expires: an abandoned upload is
    /// swept by the cleanup service without any draft/ready state machine.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Gets the remaining number of accesses available.
    /// Null if unlimited.
    /// </summary>
    public int? RemainingAccesses => MaxAccessCount.HasValue
        ? Math.Max(0, MaxAccessCount.Value - CurrentAccessCount)
        : null;

    /// <summary>
    /// Whether the access limit has been reached.
    /// </summary>
    public bool IsAccessLimitReached => MaxAccessCount.HasValue && CurrentAccessCount >= MaxAccessCount.Value;

    /// <summary>
    /// Whether the package has expired.
    /// </summary>
    public bool IsExpired(DateTimeOffset currentTime) => currentTime >= ExpiresAt;
}
