using System.Buffers;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using kroet.ShareIt.Api.Content;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Storage.S3;

/// <summary>
/// S3 implementation of the blob storage handler.
/// Stores files in S3-compatible storage with metadata support.
/// </summary>
public class S3BlobStorageHandler : IBlobStorageHandler, IDisposable
{
    /// <summary>
    /// Bodies up to this size are buffered once and stored with a single
    /// <c>PutObject</c> call. It is the largest array the shared pool keeps, so
    /// small items never allocate a large object.
    /// </summary>
    private const int ProbeBufferBytes = 1024 * 1024;

    /// <summary>
    /// Larger bodies are streamed as multipart uploads. Each part is buffered
    /// in one reusable buffer, so memory stays bounded regardless of item size.
    /// S3 requires non-final parts to be at least 5 MiB.
    /// </summary>
    private const int PartSizeBytes = 8 * 1024 * 1024;

    private static readonly ArrayPool<byte> PartBufferPool =
        ArrayPool<byte>.Create(PartSizeBytes, maxArraysPerBucket: 4);

    private static readonly TimeSpan AbortMultipartTimeout = TimeSpan.FromSeconds(30);

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
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => EnsureBucketExistsAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        Stream stream,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // The stream may be the raw request body: honor a pre-canceled token
        // before reading from it.
        cancellationToken.ThrowIfCancellationRequested();

        // Generate a unique object key. The caller-supplied label is kept in
        // the object metadata below, never in the key itself, so the key stays
        // opaque and free of client-derived characters. StorageHandler passes
        // the opaque content id here, not the original file name.
        var objectKey = Guid.NewGuid().ToString();
        var probe = ArrayPool<byte>.Shared.Rent(ProbeBufferBytes);
        byte[]? partBuffer = null;
        var objectWriteAttempted = false;

        try
        {
            var buffered = await FillAsync(
                stream,
                probe.AsMemory(0, ProbeBufferBytes),
                cancellationToken);

            if (buffered < ProbeBufferBytes)
            {
                // The whole body fits in the probe buffer: one PutObject call,
                // no multipart overhead for small items.
                objectWriteAttempted = true;
                await PutObjectAsync(objectKey, probe, buffered, contentType, fileName, cancellationToken);
                return objectKey;
            }

            // The probe is full. Read the next byte(s) into the part buffer to
            // distinguish "exactly ProbeBufferBytes" from "larger than it".
            partBuffer = PartBufferPool.Rent(PartSizeBytes);
            probe.AsSpan(0, buffered).CopyTo(partBuffer);
            buffered += await FillAsync(
                stream,
                partBuffer.AsMemory(buffered, PartSizeBytes - buffered),
                cancellationToken);

            if (buffered == ProbeBufferBytes)
            {
                objectWriteAttempted = true;
                await PutObjectAsync(objectKey, probe, buffered, contentType, fileName, cancellationToken);
                return objectKey;
            }

            objectWriteAttempted = true;
            await UploadMultipartAsync(
                objectKey,
                stream,
                partBuffer,
                buffered,
                contentType,
                fileName,
                cancellationToken);

            return objectKey;
        }
        catch (Exception ex) when (IsExpectedUploadFailure(ex, cancellationToken))
        {
            _logger.LogDebug(ex, "Upload for {FileName} ended with a client error", fileName);
            if (objectWriteAttempted)
            {
                await DeleteObjectBestEffortAsync(objectKey);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload object for file {FileName}", fileName);
            if (objectWriteAttempted)
            {
                await DeleteObjectBestEffortAsync(objectKey);
            }

            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(probe);
            if (partBuffer is not null)
            {
                PartBufferPool.Return(partBuffer);
            }
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

            // The response (and with it the HTTP connection) is released when
            // the caller disposes the stream, which the download endpoint does
            // as soon as the response body has been written or canceled.
            return new S3ObjectStream(response.ResponseStream, response as IDisposable);
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
    public async Task DeleteAsync(string blobId, CancellationToken cancellationToken = default)
    {
        try
        {
            // S3 deletes are idempotent: deleting a missing object succeeds,
            // so no HEAD probe is needed before the DELETE.
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = blobId
            };

            await _s3Client.DeleteObjectAsync(deleteRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {ObjectKey}", blobId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            await _s3Client.HeadBucketAsync(
                new HeadBucketRequest
                {
                    BucketName = _bucketName
                },
                timeout.Token);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
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
    /// Stores a body that fit in the probe buffer with a single PutObject call.
    /// </summary>
    private async Task PutObjectAsync(
        string objectKey,
        byte[] buffer,
        int length,
        string contentType,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(buffer, 0, length, writable: false, publiclyVisible: false);

        var putRequest = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType
        };
        AddObjectMetadata(putRequest.Metadata, fileName);

        await _s3Client.PutObjectAsync(putRequest, cancellationToken);
    }

    /// <summary>
    /// Streams a body of unknown length to S3 in bounded parts. The first part
    /// is already buffered; each following part reuses the same buffer, so the
    /// memory floor and ceiling are one part size. An interrupted upload is
    /// aborted, so no incomplete object is left behind.
    /// </summary>
    private async Task UploadMultipartAsync(
        string objectKey,
        Stream stream,
        byte[] partBuffer,
        int buffered,
        string contentType,
        string fileName,
        CancellationToken cancellationToken)
    {
        var initiateRequest = new InitiateMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            ContentType = contentType
        };
        AddObjectMetadata(initiateRequest.Metadata, fileName);

        var initiated = await _s3Client.InitiateMultipartUploadAsync(initiateRequest, cancellationToken);
        var uploadId = initiated.UploadId;
        var parts = new List<PartETag>();

        try
        {
            var partNumber = 1;
            var partLength = buffered;

            while (true)
            {
                var uploaded = await UploadPartAsync(
                    objectKey,
                    uploadId,
                    partNumber,
                    partBuffer,
                    partLength,
                    cancellationToken);

                parts.Add(new PartETag(partNumber, uploaded.ETag));
                partNumber++;

                partLength = await FillAsync(stream, partBuffer.AsMemory(0, PartSizeBytes), cancellationToken);
                if (partLength == 0)
                {
                    break;
                }
            }

            var completeRequest = new CompleteMultipartUploadRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
                UploadId = uploadId,
                PartETags = parts
            };

            await _s3Client.CompleteMultipartUploadAsync(completeRequest, cancellationToken);
        }
        catch
        {
            await AbortMultipartUploadAsync(objectKey, uploadId);
            throw;
        }
    }

    private async Task<UploadPartResponse> UploadPartAsync(
        string objectKey,
        string uploadId,
        int partNumber,
        byte[] buffer,
        int length,
        CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(buffer, 0, length, writable: false, publiclyVisible: false);

        var partRequest = new UploadPartRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = content,
            PartSize = length
        };

        return await _s3Client.UploadPartAsync(partRequest, cancellationToken);
    }

    /// <summary>
    /// Best-effort abort with an independent timeout: the request token may
    /// already be canceled by the client that caused the failure.
    /// </summary>
    private async Task AbortMultipartUploadAsync(string objectKey, string uploadId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(AbortMultipartTimeout);

            var abortRequest = new AbortMultipartUploadRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
                UploadId = uploadId
            };

            await _s3Client.AbortMultipartUploadAsync(abortRequest, timeout.Token);
        }
        catch (Exception ex)
        {
            // Aborts are routinely retried or already completed; this is not a
            // storage failure unless it keeps happening.
            _logger.LogWarning(ex, "Failed to abort multipart upload for object {ObjectKey}", objectKey);
        }
    }

    /// <summary>
    /// Best-effort removal of an object whose successful write was not
    /// acknowledged to the caller. Uses an independent timeout because the
    /// request token may already be canceled.
    /// </summary>
    private async Task DeleteObjectBestEffortAsync(string objectKey)
    {
        try
        {
            using var timeout = new CancellationTokenSource(AbortMultipartTimeout);

            await _s3Client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = objectKey
                },
                timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove possibly committed object {ObjectKey}", objectKey);
        }
    }

    private static bool IsExpectedUploadFailure(Exception exception, CancellationToken cancellationToken)
        => exception is ContentTooLargeException or ContentLengthMismatchException
            || exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Reads until the buffer is full or the stream ends, so a short read from
    /// a network stream is not mistaken for the end of the body.
    /// </summary>
    private static async Task<int> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        => await FillAsync(stream, buffer.AsMemory(0, buffer.Length), cancellationToken);

    private static async Task<int> FillAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static void AddObjectMetadata(MetadataCollection metadata, string fileName)
    {
        metadata["x-amz-meta-original-filename"] = fileName;
        metadata["x-amz-meta-uploaded-at"] = DateTimeOffset.UtcNow.ToString("O");
    }

    /// <summary>
    /// Disposes the S3 client.
    /// </summary>
    public void Dispose()
    {
        _s3Client.Dispose();
    }

    /// <summary>
    /// Releases the S3 response (and its HTTP connection) together with the
    /// stream. The caller only sees a plain read-only stream.
    /// </summary>
    private sealed class S3ObjectStream(Stream inner, IDisposable? owner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer)
            => inner.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                owner?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
