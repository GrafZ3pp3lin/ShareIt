using kroet.ShareIt.Api.Content;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;

namespace kroet.ShareIt.Api.Services.Storage;

/// <summary>
/// Coordinates content metadata and blob storage. Metadata deletion and
/// blob-deletion queueing are atomic in the content store. The request path
/// never waits for S3 deletion; the cleanup service drains the durable queue.
/// </summary>
public class StorageHandler : IStorageHandler
{
    private const int BlobDeletionBatchSize = 100;
    private static readonly TimeSpan OrphanCleanupTimeout = TimeSpan.FromSeconds(30);

    private const string OpaqueBlobContentType = "application/octet-stream";

    private readonly IContentStorageHandler _contentStorageHandler;
    private readonly IBlobStorageHandler _blobStorageHandler;
    private readonly ILogger<StorageHandler> _logger;

    public StorageHandler(
        IContentStorageHandler contentStorageHandler,
        IBlobStorageHandler blobStorageHandler,
        ILogger<StorageHandler> logger)
    {
        _contentStorageHandler = contentStorageHandler;
        _blobStorageHandler = blobStorageHandler;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StoreContentAsync(Api.Models.Content content, Stream dataStream, CancellationToken cancellationToken = default)
    {
        string? externalUrl = null;
        try
        {
            // The blob store must not learn the real name or MIME type: both
            // live in the opaque, client-sealed per-item metadata. Only an
            // opaque content type and the item id are written as object metadata.
            externalUrl = await _blobStorageHandler.UploadAsync(
                dataStream,
                OpaqueBlobContentType,
                content.Id.ToString(),
                cancellationToken);

            var externalContent = new ExternalContent(content, externalUrl);
            await _contentStorageHandler.StoreContentAsync(externalContent, cancellationToken);
        }
        catch (Exception ex)
        {
            if (IsExpectedClientFailure(ex, cancellationToken))
            {
                _logger.LogDebug(ex, "Content upload {ContentId} ended with a client error", content.Id);
            }
            else
            {
                _logger.LogError(ex, "Failed to store content {ContentId} (blob and/or metadata)", content.Id);
            }

            if (!string.IsNullOrEmpty(externalUrl))
            {
                // The request may have caused this failure by being canceled.
                // Give compensation its own bounded timeout so it can still run.
                try
                {
                    using var cleanupTimeout = new CancellationTokenSource(OrphanCleanupTimeout);
                    await _blobStorageHandler.DeleteAsync(externalUrl, cleanupTimeout.Token);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Failed to clean up blob {BlobId} after failed content storage", externalUrl);
                }
            }
            throw;
        }
    }

    private static bool IsExpectedClientFailure(Exception exception, CancellationToken cancellationToken)
        => exception is ContentTooLargeException or ContentLengthMismatchException or RequestBodyTruncatedException
            || exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    /// <inheritdoc />
    public async Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        var deleted = await _contentStorageHandler.DeleteContentAsync(contentId, cancellationToken);

        return deleted;
    }

    /// <inheritdoc />
    public async Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        return await _contentStorageHandler.DeletePackageAsync(packageId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
    {
        return await _contentStorageHandler.DeletePackagesAsync(packageIds, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ProcessPendingBlobDeletionsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _contentStorageHandler.GetPendingBlobDeletionsAsync(
            BlobDeletionBatchSize,
            cancellationToken);
        var completed = 0;

        foreach (var blobId in pending)
        {
            try
            {
                // A missing object already satisfies the deletion goal. S3 and
                // the in-memory handler both report that case as false.
                await _blobStorageHandler.DeleteAsync(blobId, cancellationToken);
                await _contentStorageHandler.CompleteBlobDeletionAsync(blobId, cancellationToken);
                completed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Keep the queue entry. A later cleanup run can safely repeat
                // deletion because a missing object is also success.
                _logger.LogError(ex, "Failed to delete queued blob {BlobId}; it remains queued for retry", blobId);
            }
        }

        return completed;
    }

    /// <inheritdoc />
    public async Task<Stream> RetrieveContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        var document = await _contentStorageHandler.GetContentAsync(contentId, cancellationToken);
        if (document == null)
        {
            throw new KeyNotFoundException($"Content with ID {contentId} was not found");
        }

        return await _blobStorageHandler.DownloadAsync(document.Url, cancellationToken);
    }
}
