namespace kroet.ShareIt.Api.Storage;

/// <summary>
/// Interface for blob storage operations.
/// Abstracts the underlying storage provider (Azure, AWS, GCP, etc.).
/// </summary>
public interface IBlobStorageHandler
{
    /// <summary>
    /// Uploads a blob to storage and returns a unique identifier.
    /// </summary>
    /// <param name="stream">The data stream to upload.</param>
    /// <param name="contentType">MIME type of the content.</param>
    /// <param name="fileName">Original file name (stored as object metadata, not part of the object key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unique blob identifier (e.g., blob name, key, or path).</returns>
    Task<string> UploadAsync(Stream stream, string contentType, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a blob from storage by its identifier.
    /// </summary>
    /// <param name="blobId">The unique blob identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Stream containing the blob data. The caller must dispose it; disposal
    /// also releases the provider response and its connection.
    /// </returns>
    /// <exception cref="KeyNotFoundException">Thrown when blob is not found.</exception>
    Task<Stream> DownloadAsync(string blobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob from storage.
    /// </summary>
    /// <param name="blobId">The unique blob identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string blobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a blob exists in storage.
    /// </summary>
    /// <param name="blobId">The unique blob identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the blob exists, false otherwise.</returns>
    Task<bool> ExistsAsync(string blobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes backend resources required before serving requests. The
    /// in-memory implementation uses the default no-op initialization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Performs a lightweight health check against the backing blob store.
    /// Implementations that cannot fail (for example the in-memory store) use
    /// the default healthy result.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the store is reachable, false otherwise.</returns>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

