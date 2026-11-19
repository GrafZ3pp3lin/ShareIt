namespace kroet.ShareIt.Api.Services.Storage;

/// <summary>
/// High-level storage handler that coordinates content metadata and blob
/// storage. The request path never waits for S3 deletion; the cleanup service
/// drains the durable queue.
/// </summary>
public interface IStorageHandler
{
    /// <summary>
    /// Stores content with its associated data stream. Blob upload and
    /// metadata insert are compensated on failure so no partial item remains.
    /// </summary>
    /// <param name="content">The content metadata to store.</param>
    /// <param name="dataStream">The data stream to store</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreContentAsync(Api.Models.Content content, Stream dataStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes package metadata and durably queues its blobs for deletion. The
    /// durable queue is drained later by the cleanup service.
    /// </summary>
    Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple packages and durably queues their blobs for deletion.
    /// The durable queue is drained later by the cleanup service.
    /// </summary>
    Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a batch of queued blobs. Failed objects remain queued for a
    /// later retry; missing objects are completed successfully.
    /// </summary>
    /// <returns>The number of queue entries completed.</returns>
    Task<int> ProcessPendingBlobDeletionsAsync(CancellationToken cancellationToken = default);
}
