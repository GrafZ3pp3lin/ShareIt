using kroet.ShareIt.Api.Models;

namespace kroet.ShareIt.Api.Storage;

/// <summary>
/// Provides aggregate package and content counts for the application metrics
/// snapshot. The SQL content store is the supported implementation; the
/// in-memory development store intentionally does not expose snapshots.
/// </summary>
public interface IMetricsSnapshotProvider
{
    /// <summary>
    /// Gets aggregate package and content counts. The result contains no
    /// identifiers or content metadata.
    /// </summary>
    /// <param name="now">Current time used to classify expired packages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ApplicationMetricsSnapshot> GetMetricsSnapshotAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
