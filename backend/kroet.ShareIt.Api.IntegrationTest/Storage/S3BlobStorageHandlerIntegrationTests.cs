using System.Globalization;
using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Storage.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.IntegrationTest.Storage;

[TestClass]
public class S3BlobStorageHandlerIntegrationTests
{
    private const string DefaultContentType = "text/plain";
    private const string UnreachableServiceUrl = "http://127.0.0.1:1";

    private static RustFsContainer _rustFs = null!;

    private string _bucketName = null!;
    private S3BlobStorageHandler _handler = null!;
    private AmazonS3Client _inspectionClient = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _rustFs = await RustFsContainer.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_rustFs is not null)
        {
            await _rustFs.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        _bucketName = $"shareit-tests-{Guid.NewGuid():N}";
        _handler = CreateHandler();
        await _handler.InitializeAsync();
        _inspectionClient = _rustFs.CreateClient();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _handler.Dispose();
        _inspectionClient.Dispose();
    }

    [TestMethod]
    public async Task InitializeAsync_CreatesBucket()
    {
        var bucketName = $"shareit-tests-{Guid.NewGuid():N}";
        using var handler = CreateHandler(bucketName: bucketName);

        await handler.InitializeAsync();

        var buckets = await _inspectionClient.ListBucketsAsync();
        Assert.Contains(
            bucket => bucket.BucketName == bucketName, buckets.Buckets,
            "InitializeAsync should create the configured bucket when it does not exist.");
    }

    [TestMethod]
    public async Task UploadAsync_StoresObjectWithMetadata()
    {
        var content = Encoding.UTF8.GetBytes("hello from rustfs");
        var uploadedAtLowerBound = DateTimeOffset.UtcNow;
        await using var stream = new MemoryStream(content);

        var blobId = await _handler.UploadAsync(stream, DefaultContentType, "../report final.txt");

        Assert.IsTrue(Guid.TryParse(blobId, out _), $"Expected a GUID blob id but got '{blobId}'.");

        var metadata = await _inspectionClient.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _bucketName,
            Key = blobId
        });

        Assert.AreEqual(DefaultContentType, metadata.ContentType);
        Assert.AreEqual((long)content.Length, metadata.ContentLength);
        Assert.AreEqual("../report final.txt", metadata.Metadata["x-amz-meta-original-filename"]);
        Assert.IsTrue(
            DateTimeOffset.TryParse(
                metadata.Metadata["x-amz-meta-uploaded-at"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var uploadedAt),
            "The uploaded-at metadata should be a round-trippable timestamp.");
        Assert.IsGreaterThanOrEqualTo(uploadedAtLowerBound.AddSeconds(-1), uploadedAt);
        Assert.IsLessThanOrEqualTo(DateTimeOffset.UtcNow.AddSeconds(1), uploadedAt);

        await using var downloaded = await _handler.DownloadAsync(blobId);
        using var downloadedContent = new MemoryStream();
        await downloaded.CopyToAsync(downloadedContent);
        Assert.AreSequenceEqual(content, downloadedContent.ToArray());
    }

    [TestMethod]
    public async Task UploadAsync_BodyLargerThanPartBuffer_UsesMultipartAndRoundTrips()
    {
        // Above the 1 MiB probe and the 8 MiB part size, so this takes the
        // multipart path and needs more than one part.
        var content = new byte[(9 * 1024 * 1024) + 17];
        for (var index = 0; index < content.Length; index += 4096)
        {
            content[index] = (byte)(index % 251);
        }

        await using var stream = new MemoryStream(content);

        var blobId = await _handler.UploadAsync(stream, "application/octet-stream", "large.bin");

        var metadata = await _inspectionClient.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _bucketName,
            Key = blobId
        });
        Assert.AreEqual((long)content.Length, metadata.ContentLength);

        await using var downloaded = await _handler.DownloadAsync(blobId);
        using var downloadedContent = new MemoryStream();
        await downloaded.CopyToAsync(downloadedContent);

        Assert.AreEqual(content.Length, downloadedContent.Length);
        Assert.AreEqual(content[0], downloadedContent.GetBuffer()[0]);
        Assert.AreEqual(content[^1], downloadedContent.GetBuffer()[content.Length - 1]);
        Assert.AreSequenceEqual(content, downloadedContent.ToArray());
    }


    [TestMethod]
    public async Task UploadAsync_BodyExactlyProbeBufferSize_UsesSinglePutObject()
    {
        // Exactly the probe size must not take the multipart path. A multipart
        // ETag carries a "-<part count>" suffix; a PutObject ETag does not.
        var content = new byte[1024 * 1024];
        await using var stream = new MemoryStream(content);

        var blobId = await _handler.UploadAsync(stream, "application/octet-stream", "exact.bin");

        var metadata = await _inspectionClient.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _bucketName,
            Key = blobId
        });
        Assert.AreEqual((long)content.Length, metadata.ContentLength);
        Assert.IsFalse(
            metadata.ETag.Contains('-', StringComparison.Ordinal),
            $"A body of exactly the probe size should use PutObject, but the ETag was {metadata.ETag}.");
    }

    [TestMethod]
    public async Task UploadAsync_WhenStreamFailsAfterFirstPart_AbortsMultipartAndStoresNothing()
    {
        await using var stream = new ThrowingAfterBytesStream(8 * 1024 * 1024);

        await Assert.ThrowsAsync<IOException>(
            () => _handler.UploadAsync(stream, "application/octet-stream", "aborted.bin"));

        var objects = await _inspectionClient.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = _bucketName
        });
        Assert.AreEqual(0, objects.S3Objects?.Count ?? 0);

        var uploads = await _inspectionClient.ListMultipartUploadsAsync(new ListMultipartUploadsRequest
        {
            BucketName = _bucketName
        });
        Assert.AreEqual(0, uploads.MultipartUploads?.Count ?? 0);
    }

    [TestMethod]
    public async Task UploadAsync_WhenBucketAlreadyExists_StoresBothObjects()
    {
        await _inspectionClient.PutBucketAsync(_bucketName);

        await using var firstStream = new MemoryStream([1, 2, 3]);
        var firstBlobId = await _handler.UploadAsync(firstStream, "application/octet-stream", "first.bin");
        await using var secondStream = new MemoryStream([4, 5, 6]);
        var secondBlobId = await _handler.UploadAsync(secondStream, "application/octet-stream", "second.bin");

        Assert.AreNotEqual(firstBlobId, secondBlobId);
        await AssertDownloadRoundTripsAsync(firstBlobId, [1, 2, 3]);
        await AssertDownloadRoundTripsAsync(secondBlobId, [4, 5, 6]);
    }

    [TestMethod]
    public async Task UploadAsync_WhenServiceIsUnavailable_Throws()
    {
        using var handler = CreateHandler(UnreachableServiceUrl);
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => handler.UploadAsync(stream, DefaultContentType, "unavailable.txt"));
    }

    [TestMethod]
    public async Task DownloadAsync_WhenBlobDoesNotExist_ThrowsKeyNotFoundException()
    {
        await _inspectionClient.PutBucketAsync(_bucketName);
        var blobId = Guid.NewGuid().ToString();

        var exception = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => _handler.DownloadAsync(blobId));

        Assert.Contains(blobId, exception.Message, StringComparison.Ordinal);
        var innerException = Assert.IsInstanceOfType<AmazonS3Exception>(exception.InnerException);
        Assert.AreEqual(HttpStatusCode.NotFound, innerException.StatusCode);
    }

    [TestMethod]
    public async Task DownloadAsync_WhenServiceIsUnavailable_Throws()
    {
        using var handler = CreateHandler(UnreachableServiceUrl);

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.DownloadAsync(Guid.NewGuid().ToString()));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenBlobExists_DeletesObject()
    {
        await using var stream = new MemoryStream([1, 2, 3]);
        var blobId = await _handler.UploadAsync(stream, DefaultContentType, "delete-me.txt");

        await _handler.DeleteAsync(blobId);

        await Assert.ThrowsAsync<AmazonS3Exception>(() => _inspectionClient.GetObjectMetadataAsync(
            new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = blobId
            }));
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => _handler.DownloadAsync(blobId));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenBlobDoesNotExist_SucceedsWithoutProbe()
    {
        // S3 deletes are idempotent: a missing object already satisfies the
        // deletion goal, so no HEAD probe is made before the DELETE.
        await _inspectionClient.PutBucketAsync(_bucketName);

        await _handler.DeleteAsync(Guid.NewGuid().ToString());
    }

    [TestMethod]
    public async Task DeleteAsync_WhenServiceIsUnavailable_Throws()
    {
        using var handler = CreateHandler(UnreachableServiceUrl);

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.DeleteAsync(Guid.NewGuid().ToString()));
    }

    [TestMethod]
    public async Task UploadAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.UploadAsync(stream, DefaultContentType, "cancelled.txt", cancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task DownloadAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.DownloadAsync(Guid.NewGuid().ToString(), cancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.DeleteAsync(Guid.NewGuid().ToString(), cancellationTokenSource.Token));
    }

    private async Task AssertDownloadRoundTripsAsync(string blobId, byte[] expected)
    {
        await using var downloaded = await _handler.DownloadAsync(blobId);
        using var buffer = new MemoryStream();
        await downloaded.CopyToAsync(buffer);
        Assert.AreSequenceEqual(expected, buffer.ToArray());
    }

    [TestMethod]
    public async Task CheckHealthAsync_WhenBucketExists_ReturnsTrue()
    {
        Assert.IsTrue(await _handler.CheckHealthAsync());
    }

    [TestMethod]
    public async Task CheckHealthAsync_WhenBucketDoesNotExist_ReturnsFalse()
    {
        var bucketName = $"shareit-tests-{Guid.NewGuid():N}";
        using var handler = CreateHandler(bucketName: bucketName);

        Assert.IsFalse(await handler.CheckHealthAsync());
    }

    [TestMethod]
    public async Task CheckHealthAsync_WhenServiceIsUnavailable_ReturnsFalse()
    {
        using var handler = CreateHandler(serviceUrl: UnreachableServiceUrl);

        Assert.IsFalse(await handler.CheckHealthAsync());
    }

    private S3BlobStorageHandler CreateHandler(string? serviceUrl = null, string? bucketName = null)
    {
        var settings = new S3BlobStorageSettings
        {
            AccessKeyId = RustFsContainer.AccessKeyId,
            SecretAccessKey = RustFsContainer.SecretAccessKey,
            ServiceUrl = serviceUrl ?? _rustFs.ServiceUrl,
            BucketName = bucketName ?? _bucketName
        };

        return new S3BlobStorageHandler(
            Options.Create(settings),
            NullLogger<S3BlobStorageHandler>.Instance);
    }

    private sealed class ThrowingAfterBytesStream(long byteCount) : Stream
    {
        private long _remaining = byteCount;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_remaining == 0)
            {
                throw new IOException("Simulated stream failure after the first multipart part.");
            }

            var read = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..read].Clear();
            _remaining -= read;
            return ValueTask.FromResult(read);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
