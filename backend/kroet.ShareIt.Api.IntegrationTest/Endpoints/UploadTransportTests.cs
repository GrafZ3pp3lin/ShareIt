using System.Net;
using System.Text;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Transport contract of the upload endpoint: the body is a streamed
/// <c>multipart/form-data</c> request with metadata parts before the payload,
/// and the declared size is both a limit input and part of the idempotency
/// fingerprint.
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
    public async Task Upload_WithoutSizePart_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "Id" },
            { new StringContent("file"), "Kind" },
            { new StringContent("bWV0YQ"), "Meta" },
            { new ByteArrayContent(SmallItemBytes), "File", "item.bin" }
        };

        var response = await SendRawUploadAsync(packageId, form, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid size", problem.Title);
    }

    [TestMethod]
    public async Task Upload_WithFilePartBeforeMetadata_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(SmallItemBytes), "File", "item.bin" },
            { new StringContent(Guid.NewGuid().ToString()), "Id" },
            { new StringContent("file"), "Kind" },
            { new StringContent("bWV0YQ"), "Meta" },
            { new StringContent(SmallItemBytes.Length.ToString()), "Size" }
        };

        var response = await SendRawUploadAsync(packageId, form, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content id required", problem.Title);
    }

    [TestMethod]
    public async Task Upload_WithUnknownPart_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("unexpected"), "Bogus" },
            { new StringContent(Guid.NewGuid().ToString()), "Id" },
            { new StringContent("file"), "Kind" },
            { new StringContent("bWV0YQ"), "Meta" },
            { new StringContent(SmallItemBytes.Length.ToString()), "Size" },
            { new ByteArrayContent(SmallItemBytes), "File", "item.bin" }
        };

        var response = await SendRawUploadAsync(packageId, form, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid upload", problem.Title);
        StringAssert.Contains(problem.Detail, "Bogus");
    }

    [TestMethod]
    public async Task Upload_WithNonMultipartContentType_Returns415()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        using var content = new StringContent("{\"Id\":\"ignored\"}", Encoding.UTF8, "application/json");
        var response = await SendRawUploadAsync(packageId, content, guestId);

        // The endpoint only advertises multipart/form-data, so the framework
        // rejects other content types before the handler runs.
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [TestMethod]
    public async Task Upload_WithMultipartContentTypeWithoutBoundary_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        using var content = new StringContent("not-a-multipart-body", Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/form-data");
        var response = await SendRawUploadAsync(packageId, content, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid content type", problem.Title);
    }


    [TestMethod]
    public async Task Upload_WithQuotedBoundary_Returns200()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();
        const string boundary = "quoted-boundary";
        var body = ValidMultipartBody(boundary, contentId, "hello");

        using var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"multipart/form-data; boundary=\"{boundary}\"");

        var response = await SendRawUploadAsync(packageId, content, guestId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, (await GetDatabaseCountsAsync()).ContentCount);
        Assert.AreEqual(1, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task Upload_WithTruncatedMultipartBody_Returns400AndStoresNothing()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();
        const string boundary = "truncated-boundary";
        var body = ValidMultipartBody(boundary, contentId, "hello", closingBoundary: false);

        using var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");

        var response = await SendRawUploadAsync(packageId, content, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid upload", problem.Title);
        Assert.AreEqual(0, (await GetDatabaseCountsAsync()).ContentCount);
        Assert.AreEqual(0, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task Upload_WithDuplicateMetadataPart_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        const string boundary = "duplicate-part-boundary";
        var body = ValidMultipartBody(boundary, Guid.NewGuid(), "hello")
            .Replace(
                $"Content-Disposition: form-data; name=\"Size\"\r\n\r\n5\r\n",
                $"Content-Disposition: form-data; name=\"Size\"\r\n\r\n5\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"Size\"\r\n\r\n5\r\n");

        using var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");

        var response = await SendRawUploadAsync(packageId, content, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid upload", problem.Title);
        StringAssert.Contains(problem.Detail, "Duplicate part");
        Assert.AreEqual(0, (await GetDatabaseCountsAsync()).ContentCount);
        Assert.AreEqual(0, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task Upload_WithTrailingPartAfterFile_Returns400AndStoresNothing()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        const string boundary = "trailing-part-boundary";
        var body = ValidMultipartBody(boundary, Guid.NewGuid(), "hello", closingBoundary: false) +
            $"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"Extra\"\r\n\r\nx\r\n--{boundary}--\r\n";

        using var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");

        var response = await SendRawUploadAsync(packageId, content, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid upload", problem.Title);
        Assert.AreEqual(0, (await GetDatabaseCountsAsync()).ContentCount);

        // The item was stored before the trailing section was seen; deletion
        // goes through the durable queue.
        await RunCleanupAsync();
        Assert.AreEqual(0, await CountStoredObjectsAsync());
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

    private static string ValidMultipartBody(
        string boundary,
        Guid contentId,
        string payload,
        bool closingBoundary = true)
    {
        var body = $"--{boundary}\r\n" +
            $"Content-Disposition: form-data; name=\"Id\"\r\n\r\n{contentId}\r\n" +
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"Kind\"\r\n\r\nfile\r\n" +
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"Meta\"\r\n\r\nbWV0YQ\r\n" +
            $"--{boundary}\r\n" +
            $"Content-Disposition: form-data; name=\"Size\"\r\n\r\n{Encoding.UTF8.GetByteCount(payload)}\r\n" +
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"File\"; filename=\"item.bin\"\r\n" +
            "Content-Type: application/octet-stream\r\n\r\n" +
            payload;

        return closingBoundary ? $"{body}\r\n--{boundary}--\r\n" : body;
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
}
