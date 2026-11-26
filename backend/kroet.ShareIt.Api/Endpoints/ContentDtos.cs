using System.ComponentModel.DataAnnotations;
using kroet.ShareIt.Api.Models;

namespace kroet.ShareIt.Api.Content.Dtos;

/// <summary>
/// Request to create a new content package. Content items are uploaded
/// separately through <c>PUT /package/{id}/content/{contentId}</c>.
/// </summary>
public class CreatePackageRequest
{
    /// <summary>
    /// Client-generated package ID (a GUID). The client needs it before
    /// anything is encrypted because the salt and unlock verifier derive from
    /// it. Repeating the request with the same id is idempotent for its owner
    /// when the access mode matches; code-mode retries must also use the same
    /// unlock verifier.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Base64url HMAC-SHA256 proof of the package passphrase (32 bytes).
    /// Null when the package is not protected. Required when
    /// <see cref="UseShortCode"/> is true.
    /// </summary>
    public string? UnlockVerifier { get; set; }

    /// <summary>
    /// Opts the package into code access. The server allocates a unique
    /// three-character locator, clamps the lifetime to ten minutes and
    /// <see cref="MaxAccessCount"/> to one, and returns the locator as
    /// <c>ShortCode</c>. The browser appends its own three-character secret to
    /// form the six-character code. Requires a matching
    /// <c>X-Unlock-Proof</c> header.
    /// </summary>
    public bool UseShortCode { get; set; }

    /// <summary>
    /// Maximum number of times this package can be accessed.
    /// Null means unlimited.
    /// </summary>
    public int? MaxAccessCount { get; set; }

    /// <summary>
    /// Time to live in hours (1..MaxLifetimeHours). The package expires after
    /// this duration, so abandoned uploads are always swept by cleanup.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int TimeToLiveHours { get; set; }
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

    /// <summary>
    /// Maximum number of items a package may contain. Applies to every caller.
    /// </summary>
    public required int MaxItemsPerPackage { get; init; }
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
    /// Expiration timestamp. Every package has one.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Whether the package requires an unlock proof (has a password).
    /// </summary>
    public required bool IsProtected { get; init; }

    public static ContentPackageResponse FromPackage(ContentPackage package)
    {
        return new ContentPackageResponse
        {
            Id = package.Id,
            CreatedAt = package.CreatedAt,
            ItemCount = package.Contents?.Count ?? package.ItemCount,
            MaxAccessCount = package.MaxAccessCount,
            CurrentAccessCount = package.CurrentAccessCount,
            RemainingAccesses = package.RemainingAccesses,
            ExpiresAt = package.ExpiresAt,
            IsProtected = package.UnlockVerifier is not null,
        };
    }
}

/// <summary>
/// Owner-facing package response. It adds the short code, when the owner has
/// enabled code access; recipients never see this DTO.
/// </summary>
public class ContentPackageOwnerResponse
{
    /// <summary>
    /// Package ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Three-character server locator for code access, or null for a regular
    /// link. The remaining three characters of the six-character user code are
    /// a client secret the server never sees.
    /// </summary>
    public string? ShortCode { get; init; }

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
    /// Expiration timestamp. Every package has one.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Whether the package requires an unlock proof (has a password).
    /// </summary>
    public required bool IsProtected { get; init; }

    public static ContentPackageOwnerResponse FromPackage(ContentPackage package)
    {
        return new ContentPackageOwnerResponse
        {
            Id = package.Id,
            ShortCode = package.ShortCode,
            CreatedAt = package.CreatedAt,
            ItemCount = package.Contents?.Count ?? package.ItemCount,
            MaxAccessCount = package.MaxAccessCount,
            CurrentAccessCount = package.CurrentAccessCount,
            RemainingAccesses = package.RemainingAccesses,
            ExpiresAt = package.ExpiresAt,
            IsProtected = package.UnlockVerifier is not null,
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
    /// items. Send it in the <c>X-Access-Session</c> header.
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
    /// Base64url per-item metadata blob. Tiny and needed to render names.
    /// </summary>
    public string? Meta { get; init; }
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
    /// Whether the package is reachable through a code access locator. The
    /// locator itself is not exposed: without its client secret it is not a
    /// usable code.
    /// </summary>
    public required bool HasShortCode { get; init; }

    /// <summary>
    /// When the package was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Expiration timestamp. Every package has one.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

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
            HasShortCode = summary.HasShortCode,
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

/// <summary>
/// Request to resolve a short-code locator. The three-character locator
/// travels in the body so it never appears in access logs, browser history or
/// referrers.
/// </summary>
public class ResolveShortCodeRequest
{
    /// <summary>
    /// Three-character locator as entered by the recipient; hyphens,
    /// whitespace and case are normalized before lookup.
    /// </summary>
    public string? Code { get; init; }
}

/// <summary>
/// Response for a short-code lookup. It intentionally contains only the
/// package id: the recipient still opens the package through the normal
/// unlock/access flow.
/// </summary>
public class ResolveShortCodeResponse
{
    /// <summary>
    /// Canonical package id for the code.
    /// </summary>
    public required Guid PackageId { get; init; }
}
