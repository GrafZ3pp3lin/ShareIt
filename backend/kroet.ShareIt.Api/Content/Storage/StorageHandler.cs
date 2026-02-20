using kroet.ShareIt.Api.Content.Models;
using kroet.ShareIt.Api.Content.Storage.Blobs;
using kroet.ShareIt.Api.Content.Storage.Package;

namespace kroet.ShareIt.Api.Content.Storage;

/// <summary>
/// Main storage handler that routes content to appropriate storage based on size.
/// Small content is embedded in MongoDB, large content goes to blob storage.
/// </summary>
public class StorageHandler : IStorageHandler
{
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
    public async Task StoreContentAsync(Models.Content content, Stream dataStream, CancellationToken cancellationToken = default)
    {
        string? externalUrl = null;
        try
        {
            // Store in blob storage
            externalUrl = await _blobStorageHandler.UploadAsync(
                dataStream, 
                content.MimeType, 
                content.FileName, 
                cancellationToken);
            _logger.LogDebug(
                "Stored content {ContentId} ({Size} bytes) in blob storage as {BlobId}",
                content.Id, content.Size, externalUrl);
            
            var externalContent = new ExternalContent(content, externalUrl);

            await _contentStorageHandler.StoreContentAsync(externalContent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store content {ContentId} in blob storage", content.Id);
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

