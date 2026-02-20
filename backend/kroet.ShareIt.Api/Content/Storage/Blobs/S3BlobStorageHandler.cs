using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using kroet.ShareIt.Api.Content.Configuration;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Content.Storage.Blobs;

/// <summary>
/// S3 implementation of the blob storage handler.
/// Stores files in S3-compatible storage with metadata support.
/// </summary>
public class S3BlobStorageHandler : IBlobStorageHandler, IDisposable
{
    private readonly AmazonS3Client _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<S3BlobStorageHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the S3BlobStorageHandler.
    /// </summary>
    public S3BlobStorageHandler(
        IOptions<S3BlobStorageSettings> settings,
        ILogger<S3BlobStorageHandler> logger)
    {
        _logger = logger;
        _bucketName = settings.Value.BucketName;

        var credentials = new BasicAWSCredentials(
            settings.Value.AccessKeyId,
            settings.Value.SecretAccessKey);

        var config = new AmazonS3Config
        {
            ServiceURL = settings.Value.ServiceUrl,
            ForcePathStyle = true, // Required for some S3-compatible providers
        };

        _s3Client = new AmazonS3Client(credentials, config);

        _logger.LogInformation("S3 Blob Storage handler initialized for bucket: {BucketName}, host: {URL}",
            _bucketName, settings.Value.ServiceUrl);
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
            // Ensure bucket exists
            await EnsureBucketExistsAsync(cancellationToken);

            // Generate unique object key using GUID to avoid conflicts
            var objectKey = $"{Guid.NewGuid()}/{fileName}";

            var putRequest = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
                InputStream = stream,
                ContentType = contentType,
                Metadata =
                {
                    ["x-amz-meta-original-filename"] = fileName,
                    ["x-amz-meta-uploaded-at"] = DateTimeOffset.UtcNow.ToString("O")
                }
            };

            await _s3Client.PutObjectAsync(putRequest, cancellationToken);

            _logger.LogInformation("Uploaded object {ObjectKey} with content type {ContentType}",
                objectKey, contentType);

            return objectKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload object for file {FileName}", fileName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string blobId, CancellationToken cancellationToken = default)
    {
        try
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = blobId
            };

            var response = await _s3Client.GetObjectAsync(getRequest, cancellationToken);

            _logger.LogDebug("Downloaded object {ObjectKey}", blobId);

            // Return the response stream - caller is responsible for disposing
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Object {ObjectKey} not found", blobId);
            throw new KeyNotFoundException($"Blob with ID {blobId} not found", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download object {ObjectKey}", blobId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string blobId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if object exists first
            var exists = await ExistsAsync(blobId, cancellationToken);
            
            if (!exists)
            {
                _logger.LogDebug("Object {ObjectKey} not found for deletion", blobId);
                return false;
            }

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = blobId
            };

            await _s3Client.DeleteObjectAsync(deleteRequest, cancellationToken);

            _logger.LogInformation("Deleted object {ObjectKey}", blobId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {ObjectKey}", blobId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string blobId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = blobId
            };

            await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of object {ObjectKey}", blobId);
            throw;
        }
    }

    /// <summary>
    /// Ensures the S3 bucket exists, creating it if necessary.
    /// </summary>
    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _s3Client.PutBucketAsync(_bucketName, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyOwnedByYou" || ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Bucket already exists, which is fine
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure bucket {BucketName} exists", _bucketName);
            throw;
        }
    }

    /// <summary>
    /// Disposes the S3 client.
    /// </summary>
    public void Dispose()
    {
        _s3Client.Dispose();
    }
}

