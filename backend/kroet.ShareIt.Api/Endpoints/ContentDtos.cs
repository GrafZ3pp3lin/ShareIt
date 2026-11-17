using kroet.ShareIt.Api.Models;

namespace kroet.ShareIt.Api.Content.Dtos;

/// <summary>
/// Request to create a new content package. Content items are uploaded
/// separately through <c>POST /package/{id}/upload</c>.
/// </summary>
public class CreatePackageRequest
{
    /// <summary>
    /// Client-generated package ID (a GUID). The client needs it before
    /// anything is encrypted because the salt and unlock verifier derive from
    /// it. Repeating the request with the same id is idempotent for its owner.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Base64url HMAC-SHA256 proof of the package passphrase (32 bytes).
    /// Null when the package is not protected.
    /// </summary>
    public string? UnlockVerifier { get; set; }

    /// <summary>
    /// Maximum number of times this package can be accessed.
    /// Null means unlimited.
    /// </summary>
    public int? MaxAccessCount { get; set; }

    /// <summary>
    /// Time to live in hours. Package will expire after this duration.
    /// Null means no expiration.
    /// </summary>
    public int? TimeToLiveHours { get; set; }

    /// <summary>
    /// Whether the package is private (owner-only access).
    /// </summary>
    public bool? IsPrivate { get; set; }
}

/// <summary>
/// Multipart request adding a single item (text or file bytes) to a package.
/// </summary>
public class UploadContentRequest
{
    /// <summary>
    /// Client-generated item ID. Stable across retries; a repeated upload with
    /// the same id is idempotent for the package owner.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Item kind: <c>text</c> or <c>file</c>.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Base64url per-item metadata blob (<c>{name, mime}</c>), sealed
    /// client-side when the package is protected. Never parsed by the server.
    /// </summary>
    public string? Meta { get; set; }

    /// <summary>
    /// Content bytes: sealed or plaintext, for both text and file items.
    /// </summary>
    public IFormFile? File { get; set; }
}

/// <summary>
/// Response describing the per-file upload size limits.
/// </summary>
public class UploadLimitsResponse
{
    /// <summary>
    /// Maximum size of a single uploaded file for anonymous (guest) users, in bytes.
    /// </summary>
    public required long AnonymousFileLimitBytes { get; init; }

    /// <summary>
    /// Maximum size of a single uploaded file for authenticated users, in bytes.
    /// </summary>
    public required long AuthenticatedFileLimitBytes { get; init; }

    /// <summary>
    /// Maximum package lifetime for anonymous (guest) users, in hours.
    /// </summary>
    public required int AnonymousMaxLifetimeHours { get; init; }

    /// <summary>
    /// Maximum package lifetime for authenticated users, in hours.
    /// </summary>
    public required int AuthenticatedMaxLifetimeHours { get; init; }

    /// <summary>
    /// Maximum number of times an anonymous package may be accessed.
    /// </summary>
    public required int AnonymousMaxAccessCount { get; init; }

    /// <summary>
    /// Maximum number of times an authenticated package may be accessed.
    /// </summary>
    public required int AuthenticatedMaxAccessCount { get; init; }
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
    /// When the package was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Number of items in the package.
    /// </summary>
    public required int ItemCount { get; init; }

    /// <summary>
    /// Maximum access count.
    /// </summary>
    public int? MaxAccessCount { get; init; }

    /// <summary>
    /// Number of accesses consumed so far.
    /// </summary>
    public required int CurrentAccessCount { get; init; }

    /// <summary>
    /// Remaining accesses.
    /// </summary>
    public int? RemainingAccesses { get; init; }

    /// <summary>
    /// Optional expiration timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public static ContentPackageResponse FromPackage(ContentPackage package)
    {
        return new ContentPackageResponse
        {
            Id = package.Id,
            CreatedAt = package.CreatedAt,
            ItemCount = package.Contents?.Count ?? 0,
            MaxAccessCount = package.MaxAccessCount,
            CurrentAccessCount = package.CurrentAccessCount,
            RemainingAccesses = package.RemainingAccesses,
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

    /// <summary>
    /// Short-lived grant for re-fetching this package and downloading its
    /// non-inline items. Send it in the <c>X-Access-Session</c> header.
    /// </summary>
    public required AccessSessionResponse AccessSession { get; init; }
}

/// <summary>
/// The client-visible access session.
/// </summary>
public class AccessSessionResponse
{
    /// <summary>
    /// Opaque session token. Never put this in a URL.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// When the session stops authorizing package reads and downloads.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
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
    /// Item kind: <c>text</c> or <c>file</c>. Display only — it never decides
    /// how the bytes are transported.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Size in bytes (stored length, never base64-inflated).
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// Base64url per-item metadata blob. Always inline and outside the payload
    /// budget — it is tiny and needed to render names.
    /// </summary>
    public string? Meta { get; init; }

    /// <summary>
    /// Base64url content bytes for items small enough to inline. Items without
    /// a payload are downloaded through the package's access session.
    /// </summary>
    public string? Payload { get; init; }
}

/// <summary>
/// Response for the authenticated owner's package list.
/// </summary>
public class ContentPackageListResponse
{
    /// <summary>
    /// Packages ordered newest first.
    /// </summary>
    public required List<ContentPackageListItem> Items { get; init; }

    /// <summary>
    /// Cursor for the next page, or null when the list is complete.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// One row of the owner's package list. Carries no names — names live in the
/// encrypted per-item metadata.
/// </summary>
public class ContentPackageListItem
{
    /// <summary>
    /// Package ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// When the package was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Optional expiration timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Number of items in the package.
    /// </summary>
    public required int ItemCount { get; init; }

    /// <summary>
    /// Sum of the stored item sizes, in bytes.
    /// </summary>
    public required long TotalSize { get; init; }

    /// <summary>
    /// Maximum access count.
    /// </summary>
    public int? MaxAccessCount { get; init; }

    /// <summary>
    /// Number of accesses consumed so far.
    /// </summary>
    public required int CurrentAccessCount { get; init; }

    /// <summary>
    /// Remaining accesses.
    /// </summary>
    public int? RemainingAccesses { get; init; }

    /// <summary>
    /// Whether the package has expired.
    /// </summary>
    public required bool IsExpired { get; init; }

    /// <summary>
    /// Whether the package requires an unlock proof (has a password).
    /// </summary>
    public required bool IsProtected { get; init; }

    public static ContentPackageListItem FromSummary(ContentPackageSummary summary, DateTimeOffset now)
    {
        return new ContentPackageListItem
        {
            Id = summary.Id,
            CreatedAt = summary.CreatedAt,
            ExpiresAt = summary.ExpiresAt,
            ItemCount = summary.ItemCount,
            TotalSize = summary.TotalSize,
            MaxAccessCount = summary.MaxAccessCount,
            CurrentAccessCount = summary.CurrentAccessCount,
            RemainingAccesses = summary.RemainingAccesses,
            IsExpired = summary.IsExpired(now),
            IsProtected = summary.IsProtected
        };
    }
}
