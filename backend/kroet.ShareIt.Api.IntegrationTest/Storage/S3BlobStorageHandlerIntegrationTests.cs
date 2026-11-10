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
        Assert.IsTrue(
            buckets.Buckets.Any(bucket => bucket.BucketName == bucketName),
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
        Assert.IsTrue(uploadedAt >= uploadedAtLowerBound.AddSeconds(-1));
        Assert.IsTrue(uploadedAt <= DateTimeOffset.UtcNow.AddSeconds(1));

        await using var downloaded = await _handler.DownloadAsync(blobId);
        using var downloadedContent = new MemoryStream();
        await downloaded.CopyToAsync(downloadedContent);
        CollectionAssert.AreEqual(content, downloadedContent.ToArray());
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
        Assert.IsTrue(await _handler.ExistsAsync(firstBlobId));
        Assert.IsTrue(await _handler.ExistsAsync(secondBlobId));
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

        StringAssert.Contains(exception.Message, blobId);
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
    public async Task DeleteAsync_WhenBlobExists_DeletesObjectAndReturnsTrue()
    {
        await using var stream = new MemoryStream([1, 2, 3]);
        var blobId = await _handler.UploadAsync(stream, DefaultContentType, "delete-me.txt");

        var deleted = await _handler.DeleteAsync(blobId);

        Assert.IsTrue(deleted);
        Assert.IsFalse(await _handler.ExistsAsync(blobId));
        await Assert.ThrowsAsync<AmazonS3Exception>(() => _inspectionClient.GetObjectMetadataAsync(
            new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = blobId
            }));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenBlobDoesNotExist_ReturnsFalse()
    {
        await _inspectionClient.PutBucketAsync(_bucketName);

        var deleted = await _handler.DeleteAsync(Guid.NewGuid().ToString());

        Assert.IsFalse(deleted);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenServiceIsUnavailable_Throws()
    {
        using var handler = CreateHandler(UnreachableServiceUrl);

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.DeleteAsync(Guid.NewGuid().ToString()));
    }

    [TestMethod]
    public async Task ExistsAsync_WhenBlobDoesNotExist_ReturnsFalse()
    {
        await _inspectionClient.PutBucketAsync(_bucketName);

        var exists = await _handler.ExistsAsync(Guid.NewGuid().ToString());

        Assert.IsFalse(exists);
    }

    [TestMethod]
    public async Task ExistsAsync_WhenServiceIsUnavailable_Throws()
    {
        using var handler = CreateHandler(UnreachableServiceUrl);

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.ExistsAsync(Guid.NewGuid().ToString()));
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

    [TestMethod]
    public async Task ExistsAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.ExistsAsync(Guid.NewGuid().ToString(), cancellationTokenSource.Token));
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
            AccessKeyId = _rustFs.AccessKeyId,
            SecretAccessKey = _rustFs.SecretAccessKey,
            ServiceUrl = serviceUrl ?? _rustFs.ServiceUrl,
            BucketName = bucketName ?? _bucketName
        };

        return new S3BlobStorageHandler(
            Options.Create(settings),
            NullLogger<S3BlobStorageHandler>.Instance);
    }
}
