using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using kroet.ShareIt.Api.Content.Configuration;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Content.Storage.Blobs;

/// <summary>
/// Represents a file stored in MongoDB as a document.
/// </summary>
internal class BlobDocument
{
    [BsonId]
    public ObjectId Id { get; init; }
    
    [BsonElement("fileName")]
    public required string FileName { get; set; }
    
    [BsonElement("originalFileName")]
    public required string OriginalFileName { get; set; }
    
    [BsonElement("contentType")]
    public required string ContentType { get; set; }
    
    [BsonElement("data")]
    public required byte[] Data { get; init; }
    
    [BsonElement("uploadedAt")]
    public DateTime UploadedAt { get; set; }
    
    [BsonElement("size")]
    public long Size { get; init; }
}

/// <summary>
/// MongoDB document-based implementation of the blob storage handler.
/// Stores files as BSON documents with binary data, compatible with Azure Cosmos DB for MongoDB API.
/// Suitable for files up to 16MB (BSON limit), optimized for files under 100KB.
/// </summary>
public class MongoBlobStorageHandler : IBlobStorageHandler
{
    private readonly IMongoCollection<BlobDocument> _collection;
    private readonly ILogger<MongoBlobStorageHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the MongoGridFsBlobStorageHandler.
    /// </summary>
    public MongoBlobStorageHandler(
        IOptions<MongoBlobStorageSettings> settings,
        ILogger<MongoBlobStorageHandler> logger)
    {
        _logger = logger;
        
        var mongoClient = new MongoClient(settings.Value.ConnectionString);
        var database = mongoClient.GetDatabase(settings.Value.DatabaseName);
        
        _collection = database.GetCollection<BlobDocument>(settings.Value.BucketName);
        
        _logger.LogInformation("MongoDB Blob Storage handler initialized for collection: {CollectionName} in database: {DatabaseName}",
            settings.Value.BucketName, settings.Value.DatabaseName);
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        Stream stream,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate unique filename using GUID to avoid conflicts
            var uniqueFileName = $"{Guid.NewGuid()}/{fileName}";

            // Read stream to byte array
            byte[] data;
            using (var memoryStream = new MemoryStream())
            {
                await stream.CopyToAsync(memoryStream, cancellationToken);
                data = memoryStream.ToArray();
            }

            // Check size limit (16MB BSON limit, but warn if over 100KB as per requirement)
            if (data.Length > 16 * 1024 * 1024)
            {
                throw new InvalidOperationException($"File size {data.Length} bytes exceeds MongoDB BSON document limit of 16MB");
            }
            
            if (data.Length > 100 * 1024)
            {
                _logger.LogWarning("File {FileName} size {Size} bytes exceeds recommended 100KB limit for MongoDB document storage",
                    fileName, data.Length);
            }

            // Create blob document
            var blobDocument = new BlobDocument
            {
                Id = ObjectId.GenerateNewId(),
                FileName = uniqueFileName,
                OriginalFileName = fileName,
                ContentType = contentType,
                Data = data,
                UploadedAt = DateTime.UtcNow,
                Size = data.Length
            };

            // Insert document
            await _collection.InsertOneAsync(blobDocument, cancellationToken: cancellationToken);

            _logger.LogInformation("Uploaded file to MongoDB with ID {FileId}, original name: {FileName}, size: {Size} bytes",
                blobDocument.Id, fileName, data.Length);

            return blobDocument.Id.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FileName} to MongoDB", fileName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string blobId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ObjectId.TryParse(blobId, out var objectId))
            {
                throw new KeyNotFoundException($"Invalid blob ID format: {blobId}");
            }

            // Find document by ID
            var filter = Builders<BlobDocument>.Filter.Eq(x => x.Id, objectId);
            var blobDocument = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

            if (blobDocument == null)
            {
                throw new KeyNotFoundException($"Blob with ID {blobId} not found");
            }

            // Create memory stream from byte array
            var memoryStream = new MemoryStream(blobDocument.Data);
            memoryStream.Position = 0;

            _logger.LogDebug("Downloaded file from MongoDB with ID {FileId}, size: {Size} bytes", 
                blobId, blobDocument.Size);

            return memoryStream;
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("File {BlobId} not found in MongoDB", blobId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {BlobId} from MongoDB", blobId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string blobId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ObjectId.TryParse(blobId, out var objectId))
            {
                _logger.LogWarning("Invalid blob ID format for deletion: {BlobId}", blobId);
                return false;
            }

            // Delete document by ID
            var filter = Builders<BlobDocument>.Filter.Eq(x => x.Id, objectId);
            var result = await _collection.DeleteOneAsync(filter, cancellationToken);

            if (result.DeletedCount > 0)
            {
                _logger.LogInformation("Deleted file from MongoDB with ID {FileId}", blobId);
                return true;
            }
            else
            {
                _logger.LogDebug("File {BlobId} not found for deletion in MongoDB", blobId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {BlobId} from MongoDB", blobId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string blobId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ObjectId.TryParse(blobId, out var objectId))
            {
                return false;
            }

            var filter = Builders<BlobDocument>.Filter.Eq(x => x.Id, objectId);
            var count = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of file {BlobId} in MongoDB", blobId);
            throw;
        }
    }
}

