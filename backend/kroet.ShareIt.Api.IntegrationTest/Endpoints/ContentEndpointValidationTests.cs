using System.Buffers.Text;
using System.Net;
using Amazon.S3.Model;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint validation, not-found, and download error paths, running against
/// the real Postgres and RustFS containers.
/// </summary>
[TestClass]
public sealed class ContentEndpointValidationTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task UploadContent_WithoutFile_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadWithoutFileAsync(packageId, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Empty content", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithoutIdentity_Returns400()
    {
        var packageId = await CreatePackageAsync(guestId: Guid.NewGuid());

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_ForUnknownPackage_Returns404()
    {
        var response = await SendUploadItemAsync(Guid.NewGuid(), "file", SmallItemBytes, guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithEmptyFile_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "file", [], guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Empty content", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithInvalidKind_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "video", SmallItemBytes, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid kind", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithInvalidMeta_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes, meta: "not base64url!!", guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid meta", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithOversizedMeta_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var oversizedMeta = Base64Url.EncodeToString(new byte[4097]);

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes, meta: oversizedMeta, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Meta too large", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_StoresItemInPostgresAndRustFsWithOpaqueMetadata()
    {
        var guestId = Guid.NewGuid();
        var contentId = Guid.NewGuid();
        var meta = MetaFor("secret-name.txt", "application/pdf");
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, meta, contentId, guestId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        var stored = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(packageId, includeContent: true);

        Assert.IsNotNull(stored);
        var storedContents = stored.Contents;
        Assert.IsNotNull(storedContents);
        Assert.HasCount(1, storedContents);
        var storedContent = storedContents.Single();
        Assert.AreEqual(contentId, storedContent.Id);
        Assert.AreEqual(meta, storedContent.Meta, "The server must store the opaque metadata verbatim.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(storedContent.Url));

        // The blob store must not learn the real name or MIME type.
        var metadata = await S3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = BucketName,
            Key = storedContent.Url
        });
        Assert.AreEqual("application/octet-stream", metadata.ContentType);
        Assert.AreEqual(contentId.ToString(), metadata.Metadata["x-amz-meta-original-filename"]);
        Assert.IsFalse(metadata.Metadata["x-amz-meta-original-filename"].Contains("secret-name", StringComparison.Ordinal));

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        Assert.AreEqual(meta, details.Items.Single().Meta);
    }

    [TestMethod]
    public async Task GetPackage_MetaIsReturnedVerbatimAndNeverParsed()
    {
        var guestId = Guid.NewGuid();

        // Deliberately not JSON: the server must not inspect the blob.
        var meta = Base64Url.EncodeToString("this is not json"u8.ToArray());
        var (packageId, _) = await CreatePackageWithItemAsync(meta: meta, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        Assert.AreEqual(meta, details.Items.Single().Meta);
    }

    [TestMethod]
    public async Task DownloadContent_WithBlankToken_Returns400()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);
        var file = await GetFileDownloadInfoAsync(packageId, guestId);

        var response = await SendDownloadAsync(file.DownloadUrl, token: " ", guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Missing download token", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithoutIdentity_Returns400()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);
        var file = await GetFileDownloadInfoAsync(packageId, guestId);

        var response = await SendDownloadAsync(file.DownloadUrl, file.DownloadToken, guestId: null);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithTokenForDifferentContent_Returns400()
    {
        var guestId = Guid.NewGuid();
        var (firstPackageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);
        var (secondPackageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var firstFile = await GetFileDownloadInfoAsync(firstPackageId, guestId);
        var secondFile = await GetFileDownloadInfoAsync(secondPackageId, guestId);

        var response = await SendDownloadAsync(secondFile.DownloadUrl, firstFile.DownloadToken, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Token mismatch", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithTokenForContentFromDifferentPackage_Returns404()
    {
        var guestId = Guid.NewGuid();
        var (firstPackageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);
        var (secondPackageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var firstFile = await GetFileDownloadInfoAsync(firstPackageId, guestId);
        var foreignUrl = $"/api/v1/content/package/{secondPackageId}/content/{firstFile.ContentId}/download";

        var response = await SendDownloadAsync(foreignUrl, firstFile.DownloadToken, guestId);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content not in package", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WhenBlobIsMissing_Returns404()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);
        var file = await GetFileDownloadInfoAsync(packageId, guestId);

        var content = await Services.GetRequiredService<IContentStorageHandler>()
            .GetContentAsync(contentId);
        Assert.IsNotNull(content);

        await S3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = content.Url
        });

        var response = await SendDownloadAsync(file.DownloadUrl, file.DownloadToken, guestId);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content not found", problem.Title);
    }

    private async Task<(Guid ContentId, string DownloadUrl, string DownloadToken)> GetFileDownloadInfoAsync(
        Guid packageId,
        Guid guestId)
    {
        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var file = details.Items.Single(item => item.Kind == "file");

        Assert.IsNotNull(file.DownloadUrl);
        Assert.IsNotNull(file.DownloadToken);

        return (file.Id, file.DownloadUrl, file.DownloadToken);
    }
}
