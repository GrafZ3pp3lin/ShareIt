using System.Net;
using System.Net.Http.Headers;
using System.Text;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Transport contract of the upload endpoint: a raw binary PUT whose
/// <c>Content-Length</c> is both the limit input and part of the idempotency
/// fingerprint, with metadata in the <c>X-Content-Kind</c> and
/// <c>X-Content-Meta</c> headers.
/// </summary>
[TestClass]
public sealed class UploadTransportTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task Upload_WithDeclaredSizeLargerThanPayload_Returns400AndStoresNothing()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(
            packageId,
            "file",
            SmallItemBytes,
            declaredSize: SmallItemBytes.Length + 1,
            guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content size mismatch", problem.Title);

        Assert.AreEqual(0, (await GetDatabaseCountsAsync()).ContentCount);
        Assert.AreEqual(0, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task Upload_WithDeclaredSizeSmallerThanPayload_Returns400AndStoresNothing()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(
            packageId,
            "file",
            SmallItemBytes,
            declaredSize: SmallItemBytes.Length - 1,
            guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content size mismatch", problem.Title);

        Assert.AreEqual(0, (await GetDatabaseCountsAsync()).ContentCount);
        Assert.AreEqual(0, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task Upload_WithoutContentLength_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        // A non-seekable body forces chunked transfer encoding: no
        // Content-Length header reaches the endpoint.
        using var content = new StreamContent(new NonSeekableStream(SmallItemBytes));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/content/package/{packageId}/content/{Guid.NewGuid()}")
        {
            Content = content
        };
        request.Headers.TransferEncodingChunked = true;
        request.Headers.Add("X-Content-Kind", "file");
        request.Headers.Add("X-Content-Meta", "bWV0YQ");
        AddIdentity(request, guestId, null);

        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content length required", problem.Title);
    }

    [TestMethod]
    public async Task Upload_WithNonOctetStreamContentType_Returns415()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        using var content = new ByteArrayContent(SmallItemBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var response = await SendRawUploadAsync(
            packageId, Guid.NewGuid(), "file", "bWV0YQ", content, guestId);

        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid content type", problem.Title);
    }

    [TestMethod]
    public async Task Upload_WithoutKindHeader_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        using var content = new ByteArrayContent(SmallItemBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await SendRawUploadAsync(
            packageId, Guid.NewGuid(), kind: null, "bWV0YQ", content, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid kind", problem.Title);
    }

    [TestMethod]
    public async Task Upload_WithoutMetaHeader_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        using var content = new ByteArrayContent(SmallItemBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await SendRawUploadAsync(
            packageId, Guid.NewGuid(), "file", meta: null, content, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid meta", problem.Title);
    }

    [TestMethod]
    public async Task Upload_WithTinyDeclaredSizeAndLargePayload_Returns400AndStoresNothing()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(
            packageId,
            "file",
            new byte[(9 * 1024 * 1024) + 1],
            declaredSize: 1,
            guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content size mismatch", problem.Title);
        Assert.AreEqual(0, (await GetDatabaseCountsAsync()).ContentCount);
        Assert.AreEqual(0, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task Upload_ReusingItemIdWithDifferentSize_Returns409()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();

        var first = await SendUploadItemAsync(packageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var conflict = await SendUploadItemAsync(
            packageId,
            "file",
            new byte[SmallItemBytes.Length + 1],
            contentId: contentId,
            guestId: guestId);

        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await ReadProblemAsync(conflict);
        Assert.AreEqual("Content id reused with different content", problem.Title);

        Assert.AreEqual(1, (await GetDatabaseCountsAsync()).ContentCount);
        Assert.AreEqual(1, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task Upload_ReusingItemIdWithDifferentMeta_Returns409()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();

        var first = await SendUploadItemAsync(
            packageId,
            "file",
            SmallItemBytes,
            meta: MetaFor("first.bin", "application/octet-stream"),
            contentId: contentId,
            guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var conflict = await SendUploadItemAsync(
            packageId,
            "file",
            SmallItemBytes,
            meta: MetaFor("second.bin", "application/octet-stream"),
            contentId: contentId,
            guestId: guestId);

        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await ReadProblemAsync(conflict);
        Assert.AreEqual("Content id reused with different content", problem.Title);
    }

    [TestMethod]
    public async Task Upload_ReusingItemIdWithDifferentKind_Returns409()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();

        var first = await SendUploadItemAsync(packageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var conflict = await SendUploadItemAsync(packageId, "text", SmallItemBytes, contentId: contentId, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await ReadProblemAsync(conflict);
        Assert.AreEqual("Content id reused with different content", problem.Title);
    }

    /// <summary>
    /// Wraps a byte array in a non-seekable stream so the HTTP client cannot
    /// compute a Content-Length and falls back to chunked transfer encoding.
    /// </summary>
    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private int _position;

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
        {
            var remaining = bytes.Length - _position;
            var toCopy = Math.Min(count, remaining);
            Array.Copy(bytes, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var remaining = bytes.Length - _position;
            var toCopy = Math.Min(count, remaining);
            Array.Copy(bytes, _position, buffer, offset, toCopy);
            _position += toCopy;
            return Task.FromResult(toCopy);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
