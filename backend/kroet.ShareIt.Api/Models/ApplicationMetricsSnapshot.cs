namespace kroet.ShareIt.Api.Models;

/// <summary>
/// Point-in-time aggregate counts used for the application's observable
/// gauges. It contains no package, user or content identifiers.
/// </summary>
public sealed record ApplicationMetricsSnapshot
{
    /// <summary>
    /// Total number of packages currently stored.
    /// </summary>
    public required int TotalPackages { get; init; }

    /// <summary>
    /// Packages that are neither expired nor have reached their access limit.
    /// </summary>
    public required int ActivePackages { get; init; }

    /// <summary>
    /// Packages whose expiry has passed. They may still be present until the
    /// cleanup service removes them.
    /// </summary>
    public required int ExpiredPackages { get; init; }

    /// <summary>
    /// Packages that have reached their access limit. They may still be
    /// present until the cleanup service removes them.
    /// </summary>
    public required int ExhaustedPackages { get; init; }

    /// <summary>
    /// Packages that require an unlock proof.
    /// </summary>
    public required int ProtectedPackages { get; init; }

    /// <summary>
    /// Packages marked as private.
    /// </summary>
    public required int PrivatePackages { get; init; }

    /// <summary>
    /// Packages without any content items.
    /// </summary>
    public required int EmptyPackages { get; init; }

    /// <summary>
    /// Total number of stored content items.
    /// </summary>
    public required int TotalItems { get; init; }

    /// <summary>
    /// Sum of the logical stored item sizes, in bytes.
    /// </summary>
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Packages the cleanup service should remove: expired or out of accesses.
    /// </summary>
    public required int CleanupPending { get; init; }
}
