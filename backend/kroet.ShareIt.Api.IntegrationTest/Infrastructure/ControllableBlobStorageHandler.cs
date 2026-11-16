using kroet.ShareIt.Api.Storage;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Delegates to the real blob storage handler but can fail a configurable
/// <see cref="DeleteAsync"/> call to exercise the blob-deletion queue retries.
/// </summary>
internal sealed class ControllableBlobStorageHandler(IBlobStorageHandler inner) : IBlobStorageHandler
{
    private int _deleteCallCount;

    public int? FailOnDeleteCall { get; set; }

    public int DeleteCallCount => Volatile.Read(ref _deleteCallCount);

    public Task<string> UploadAsync(
        Stream stream,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
        => inner.UploadAsync(stream, contentType, fileName, cancellationToken);

    public Task<Stream> DownloadAsync(string blobId, CancellationToken cancellationToken = default)
        => inner.DownloadAsync(blobId, cancellationToken);

    public Task<bool> DeleteAsync(string blobId, CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _deleteCallCount);
        if (FailOnDeleteCall == call)
        {
            throw new InvalidOperationException("Simulated blob deletion failure.");
        }

        return inner.DeleteAsync(blobId, cancellationToken);
    }

    public Task<bool> ExistsAsync(string blobId, CancellationToken cancellationToken = default)
        => inner.ExistsAsync(blobId, cancellationToken);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => inner.InitializeAsync(cancellationToken);

    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
        => inner.CheckHealthAsync(cancellationToken);
}
