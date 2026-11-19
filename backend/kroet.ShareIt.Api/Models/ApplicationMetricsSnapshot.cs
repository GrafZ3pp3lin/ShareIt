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
    /// Packages that are not cleanup-eligible: not expired, and not exhausted
    /// beyond the download-retention window. Exhausted packages still inside
    /// that window count as active.
    /// </summary>
    public required int ActivePackages { get; init; }

    /// <summary>
    /// Packages whose expiry has passed. They may still be present until the
    /// cleanup service removes them.
    /// </summary>
    public required int ExpiredPackages { get; init; }

    /// <summary>
    /// Packages that have reached their access limit. They may still be
    /// present after retention elapses until the cleanup service removes them.
    /// </summary>
    public required int ExhaustedPackages { get; init; }

    /// <summary>
    /// Packages that require an unlock proof.
    /// </summary>
    public required int ProtectedPackages { get; init; }

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
    /// Number of blob-deletion queue entries waiting for the cleanup service.
    /// </summary>
    public required int PendingBlobDeletions { get; init; }

    /// <summary>
    /// Packages eligible for cleanup at snapshot time: expired, or exhausted
    /// beyond the download-retention window. This is not the blob-queue depth.
    /// </summary>
    public required int CleanupPending { get; init; }
}
