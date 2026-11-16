namespace kroet.ShareIt.Api.Services.Storage;

/// <summary>
/// High-level storage handler that manages content storage with automatic routing
/// to the content store (embedded) or Blob Storage based on size.
/// </summary>
public interface IStorageHandler
{
    /// <summary>
    /// Stores content with its associated data stream.
    /// </summary>
    /// <param name="content">The content metadata to store.</param>
    /// <param name="dataStream">The data stream to store</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreContentAsync(Api.Models.Content content, Stream dataStream, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes content metadata and durably queues its blob for deletion. The
    /// durable queue is drained later by the cleanup service.
    /// </summary>
    /// <param name="contentId">The unique content identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default);

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
    
    /// <summary>
    /// Retrieves the content data stream.
    /// </summary>
    /// <param name="contentId">The unique content identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream containing the content data.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when content is not found.</exception>
    Task<Stream> RetrieveContentAsync(Guid contentId, CancellationToken cancellationToken = default);
}