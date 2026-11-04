using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;

namespace kroet.ShareIt.Api.Services.Storage;

/// <summary>
/// Main storage handler that routes content to appropriate storage based on size.
/// Small content is embedded in the content store, large content goes to blob storage.
/// </summary>
public class StorageHandler : IStorageHandler
{
    /// <summary>
    /// Content type written to the blob store for every item. The real MIME
    /// type is part of the encrypted per-item metadata, never storage metadata.
    /// </summary>
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
            // opaque content type and the item id are written as object
            // metadata.
            externalUrl = await _blobStorageHandler.UploadAsync(
                dataStream,
                OpaqueBlobContentType,
                content.Id.ToString(),
                cancellationToken);
            _logger.LogDebug(
                "Stored content {ContentId} ({Size} bytes) in blob storage as {BlobId}",
                content.Id, content.Size, externalUrl);
            
            var externalContent = new ExternalContent(content, externalUrl);

            await _contentStorageHandler.StoreContentAsync(externalContent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store content {ContentId} (blob and/or metadata)", content.Id);
            if (!string.IsNullOrEmpty(externalUrl))
            {
                // Attempt to clean up the uploaded blob
                try
                {
                    await _blobStorageHandler.DeleteAsync(externalUrl, cancellationToken);
                    _logger.LogDebug("Cleaned up blob {BlobId} after failed content storage", externalUrl);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Failed to clean up blob {BlobId} after failed content storage", externalUrl);
                }
            }
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        // First get the document to check if there's blob storage to clean up
        var document = await _contentStorageHandler.GetContentAsync(contentId, cancellationToken);

        if (document == null)
        {
            return false;
        }

        // Delete blob if exists
        try
        {
            await _blobStorageHandler.DeleteAsync(document.Url, cancellationToken);
            _logger.LogDebug("Deleted blob {BlobId} for content {ContentId}", document.Url, contentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete blob {BlobId} for content {ContentId}", document.Url, contentId);
            return false;
        }

        // Delete the document from internal storage
        var deleted = await _contentStorageHandler.DeleteContentAsync(contentId, cancellationToken);

        if (deleted)
        {
            _logger.LogInformation("Deleted content {ContentId}", contentId);
        }

        return deleted;
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

