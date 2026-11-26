namespace kroet.ShareIt.Api.Models;

/// <summary>
/// Aggregate row for the owner's package list: the package shell plus its
/// content count and total stored size.
/// </summary>
public sealed record ContentPackageSummary
{
    /// <summary>
    /// Package ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Whether the package is reachable through a code access locator.
    /// The locator itself is deliberately not exposed in list rows: without
    /// the client secret it is not a usable code.
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
    /// Maximum access count. Null means unlimited.
    /// </summary>
    public int? MaxAccessCount { get; init; }

    /// <summary>
    /// Number of accesses consumed so far.
    /// </summary>
    public required int CurrentAccessCount { get; init; }

    /// <summary>
    /// Remaining accesses. Null means unlimited.
    /// </summary>
    public int? RemainingAccesses => MaxAccessCount.HasValue
        ? Math.Max(0, MaxAccessCount.Value - CurrentAccessCount)
        : null;

    /// <summary>
    /// Whether the package requires an unlock proof (has a stored verifier).
    /// </summary>
    public required bool IsProtected { get; init; }

    /// <summary>
    /// Whether the package has expired.
    /// </summary>
    public bool IsExpired(DateTimeOffset currentTime) => currentTime >= ExpiresAt;
}

/// <summary>
/// One page of the owner's packages, ordered newest first.
/// </summary>
public sealed record PackageListPage(
    IReadOnlyList<ContentPackageSummary> Items,
    string? NextCursor);
