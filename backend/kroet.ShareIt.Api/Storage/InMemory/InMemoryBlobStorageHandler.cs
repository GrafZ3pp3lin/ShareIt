namespace kroet.ShareIt.Api.Storage.InMemory;

internal record InMemoryContentBlob
{
    public required Guid Id { get; init; }
    public required byte[] Data { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}

public class InMemoryBlobStorageHandler : IBlobStorageHandler
{
    private readonly Dictionary<Guid, InMemoryContentBlob> _blobs = new();

    public Task<string> UploadAsync(Stream stream, string contentType, string fileName, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);

        var blob = new InMemoryContentBlob
        {
            Id = Guid.NewGuid(),
            Data = memoryStream.ToArray(),
            ContentType = contentType,
            FileName = fileName
        };
        
        _blobs.Add(blob.Id, blob);
        
        return Task.FromResult(blob.Id.ToString());
    }

    public Task<Stream> DownloadAsync(string blobId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(blobId, out var id) || !_blobs.TryGetValue(id, out var blob))
        {
            throw new FileNotFoundException($"Blob with ID {blobId} not found.");
        }

        return Task.FromResult<Stream>(new MemoryStream(blob.Data));
    }

    public Task<bool> DeleteAsync(string blobId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(blobId, out var id))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_blobs.Remove(id));
    }

    public Task<bool> ExistsAsync(string blobId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(blobId, out var id))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_blobs.ContainsKey(id));
    }
}