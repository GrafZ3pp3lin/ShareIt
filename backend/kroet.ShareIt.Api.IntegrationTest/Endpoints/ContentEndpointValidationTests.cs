using System.Net;
using Amazon.S3.Model;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Models;
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
    private static readonly byte[] FileBytes = "hello world"u8.ToArray();

    [TestMethod]
    public async Task CreatePackage_WithFileAndInitialText_StoresInPostgresAndRustFs()
    {
        var guestId = Guid.NewGuid();

        var response = await SendCreatePackageAsync(
            guestId: guestId,
            files:
            [
                (Array.Empty<byte>(), "empty.txt"),
                (FileBytes, "hello.txt")
            ],
            texts: ["shared text", "   "]);

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);

        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(2, package.ItemCount, "Blank text and empty files must be skipped.");

        var stored = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(package.Id, includeContent: true);

        Assert.IsNotNull(stored);
        var storedContents = stored.Contents!;
        Assert.HasCount(2, storedContents);

        var storedFile = storedContents.Single(content => content.ContentType == ContentType.File);
        var storedText = storedContents.Single(content => content.ContentType == ContentType.Text);
        Assert.AreEqual("hello.txt", storedFile.FileName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(storedFile.Url));
        Assert.IsFalse(string.IsNullOrWhiteSpace(storedText.Url));

        var metadata = await S3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = BucketName,
            Key = storedFile.Url
        });
        Assert.AreEqual("text/plain", metadata.ContentType);
        Assert.AreEqual("hello.txt", metadata.Metadata["x-amz-meta-original-filename"]);

        var details = await GetPackageDetailsAsync(package.Id, guestId: guestId);
        Assert.HasCount(2, details.Items);
        Assert.AreEqual("shared text", details.Items.Single(item => item.Type == "text").Text);

        var fileItem = details.Items.Single(item => item.Type == "file");
        Assert.IsNotNull(fileItem.DownloadUrl);
        Assert.IsNotNull(fileItem.DownloadToken);

        var download = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        var downloaded = await download.Content.ReadAsByteArrayAsync();
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode);
        CollectionAssert.AreEqual(FileBytes, downloaded);
    }

    [TestMethod]
    public async Task UploadFilesToPackage_WithoutFiles_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var response = await SendUploadWithoutFilesAsync(packageId, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("No files provided", problem.Title);
    }

    [TestMethod]
    public async Task UploadFilesToPackage_WithoutIdentity_Returns400()
    {
        var packageId = await CreatePackageAsync(guestId: Guid.NewGuid(), fileBytes: FileBytes);

        var response = await SendUploadFilesAsync(packageId, [1, 2, 3]);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }

    [TestMethod]
    public async Task UploadFilesToPackage_ForUnknownPackage_Returns404()
    {
        var response = await SendUploadFilesAsync(Guid.NewGuid(), [1, 2, 3], guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    [TestMethod]
    public async Task UploadFilesToPackage_AsOwner_StoresEveryFileInPostgresAndRustFs()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var response = await SendUploadFilesAsync(
            packageId,
            [([1, 2, 3], "one.bin"), ([4, 5, 6], "two.bin")],
            guestId);

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(3, package.ItemCount);

        var stored = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(packageId, includeContent: true);
        Assert.IsNotNull(stored);
        Assert.HasCount(3, stored.Contents!);
        Assert.AreEqual(3, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task UploadFilesToPackage_WithEmptyFile_SkipsOnlyTheEmptyFile()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var response = await SendUploadFilesAsync(
            packageId,
            [(Array.Empty<byte>(), "empty.bin"), ([1, 2, 3], "real.bin")],
            guestId);

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(2, package.ItemCount);
        Assert.AreEqual(2, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task AddTextToPackage_WithEmptyText_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var response = await SendAddTextAsync(packageId, "   ", guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid text", problem.Title);
    }

    [TestMethod]
    public async Task AddTextToPackage_WithoutIdentity_Returns400()
    {
        var packageId = await CreatePackageAsync(guestId: Guid.NewGuid(), fileBytes: FileBytes);

        var response = await SendAddTextAsync(packageId, "some text");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }

    [TestMethod]
    public async Task AddTextToPackage_ForUnknownPackage_Returns404()
    {
        var response = await SendAddTextAsync(Guid.NewGuid(), "some text", guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    [TestMethod]
    public async Task AddTextToPackage_AsOwner_StoresTextInPostgresAndRustFs()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var response = await SendAddTextAsync(packageId, "added later", guestId: guestId);

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(2, package.ItemCount);

        var stored = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(packageId, includeContent: true);
        Assert.IsNotNull(stored);
        Assert.HasCount(2, stored.Contents!);
        Assert.AreEqual(2, await CountStoredObjectsAsync());

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        Assert.AreEqual("added later", details.Items.Single(item => item.Type == "text").Text);
    }

    [TestMethod]
    public async Task DownloadContent_WithBlankToken_Returns400()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);
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
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);
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
        var firstPackageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);
        var secondPackageId = await CreatePackageAsync(
            guestId: guestId,
            fileBytes: "second file"u8.ToArray(),
            fileName: "second.txt");

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
        var firstPackageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);
        var secondPackageId = await CreatePackageAsync(
            guestId: guestId,
            fileBytes: "second file"u8.ToArray(),
            fileName: "second.txt");

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
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);
        var file = await GetFileDownloadInfoAsync(packageId, guestId);

        var content = await Services.GetRequiredService<IContentStorageHandler>()
            .GetContentAsync(file.ContentId);
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
        var file = details.Items.Single(item => item.Type == "file");

        Assert.IsNotNull(file.DownloadUrl);
        Assert.IsNotNull(file.DownloadToken);

        return (file.Id, file.DownloadUrl, file.DownloadToken);
    }
}
