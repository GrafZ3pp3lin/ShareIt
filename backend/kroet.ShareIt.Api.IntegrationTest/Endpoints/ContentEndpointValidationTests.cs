using System.Buffers.Text;
using System.Net;
using Amazon.S3.Model;
using Dapper;
using kroet.ShareIt.Api.Content;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint validation, not-found, and download error paths, running against
/// the real Postgres and RustFS containers.
/// </summary>
[TestClass]
public sealed class ContentEndpointValidationTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task UploadContent_WithEmptyBody_Returns400()
    {
        var packageId = await CreatePackageAsync();

        var response = await SendUploadItemAsync(packageId, "file", []);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Empty content", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithoutUploadToken_Returns400()
    {
        var packageId = await CreatePackageAsync();

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes, uploadToken: " ");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Missing upload token", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_ForUnknownPackage_Returns404()
    {
        var response = await SendUploadItemAsync(Guid.NewGuid(), "file", SmallItemBytes);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_ToExpiredPackage_Returns410()
    {
        var packageId = await CreatePackageAsync(timeToLiveHours: 1);

        TimeProvider.SetUtcNow(TimeProvider.GetUtcNow().AddHours(2));

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes);

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package expired", problem.Title);
        Assert.AreEqual(0, (await GetDatabaseCountsAsync()).ContentCount);
    }

    [TestMethod]
    public async Task CreatePackage_RetryAfterExpiry_Returns410()
    {
        var created = await CreatePackageOwnerAsync(timeToLiveHours: 1);

        TimeProvider.SetUtcNow(TimeProvider.GetUtcNow().AddHours(2));

        var response = await SendCreatePackageAsync(
            packageId: created.Id,
            timeToLiveHours: 1,
            uploadToken: created.UploadToken);

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package expired", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithInvalidKind_Returns400()
    {
        var packageId = await CreatePackageAsync();

        var response = await SendUploadItemAsync(packageId, "video", SmallItemBytes);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid kind", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithInvalidMeta_Returns400()
    {
        var packageId = await CreatePackageAsync();

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes, meta: "not base64url!!");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid meta", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithOversizedMeta_Returns400()
    {
        var packageId = await CreatePackageAsync();
        var oversizedMeta = Base64Url.EncodeToString(new byte[4097]);

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes, meta: oversizedMeta);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Meta too large", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_StoresItemInPostgresAndRustFsWithOpaqueMetadata()
    {
        var contentId = Guid.NewGuid();
        var meta = MetaFor("secret-name.txt", "application/pdf");
        var packageId = await CreatePackageAsync();

        var response = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, meta, contentId);
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

        var details = await GetPackageDetailsAsync(packageId);
        Assert.AreEqual(meta, details.Items.Single().Meta);
    }

    [TestMethod]
    public async Task GetPackage_MetaIsReturnedVerbatimAndNeverParsed()
    {
        // Deliberately not JSON: the server must not inspect the blob.
        var meta = Base64Url.EncodeToString("this is not json"u8.ToArray());
        var (packageId, _) = await CreatePackageWithItemAsync(meta: meta);

        var details = await GetPackageDetailsAsync(packageId);

        Assert.AreEqual(meta, details.Items.Single().Meta);
    }

    [TestMethod]
    public async Task DownloadContent_WithBlankSession_Returns400()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);

        var response = await SendDownloadAsync(DownloadUrl(packageId, contentId), accessSession: " ");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Missing access session", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithoutAuthentication_IsAllowedForGuestSessions()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);
        var details = await GetPackageDetailsAsync(packageId);

        var response = await SendDownloadAsync(
            DownloadUrl(packageId, contentId),
            details.AccessSession.Token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreSequenceEqual(LargeItemBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task DownloadContent_WithGuestCallerForAnAuthenticatedSession_Returns401()
    {
        var ownerId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(
            bytes: LargeItemBytes, userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);
        var details = await GetPackageDetailsAsync(packageId, userId: ownerId);

        // The session is bound to the authenticated owner; an unauthenticated
        // (guest) caller must not be able to use it.
        var response = await SendDownloadAsync(
            DownloadUrl(packageId, contentId),
            details.AccessSession.Token);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid access session", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithSessionForDifferentPackage_Returns401()
    {
        var (firstPackageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);
        var (secondPackageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);

        var firstDetails = await GetPackageDetailsAsync(firstPackageId);
        var secondDetails = await GetPackageDetailsAsync(secondPackageId);

        var response = await SendDownloadAsync(
            DownloadUrl(secondPackageId, secondDetails.Items.Single().Id),
            firstDetails.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid access session", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithContentFromDifferentPackage_Returns404()
    {
        var (_, firstContentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);
        var secondPackageId = await CreatePackageAsync();
        var secondUpload = await SendUploadItemAsync(
            secondPackageId, "file", LargeItemBytes);
        Assert.AreEqual(HttpStatusCode.OK, secondUpload.StatusCode);

        var secondDetails = await GetPackageDetailsAsync(secondPackageId);

        var response = await SendDownloadAsync(
            DownloadUrl(secondPackageId, firstContentId),
            secondDetails.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content not in package", problem.Title);
        Assert.AreNotEqual(
            ProblemTypes.PackageGone,
            problem.Type,
            "A content-level 404 must not be classified as package-gone.");
    }

    [TestMethod]
    public async Task DownloadContent_WhenBlobIsMissing_Returns404()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);
        var details = await GetPackageDetailsAsync(packageId);

        var content = await Services.GetRequiredService<IContentStorageHandler>()
            .GetContentAsync(contentId);
        Assert.IsNotNull(content);

        await S3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = content.Url
        });

        var response = await SendDownloadAsync(
            DownloadUrl(packageId, contentId),
            details.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Content not found", problem.Title);
        Assert.AreNotEqual(
            ProblemTypes.PackageGone,
            problem.Type,
            "A missing blob must not clear the package session.");
    }

    [TestMethod]
    public async Task DownloadContent_WhenSessionHasExpired_Returns401()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);
        var details = await GetPackageDetailsAsync(packageId);

        TimeProvider.SetUtcNow(details.AccessSession.ExpiresAt.AddSeconds(1));

        var response = await SendDownloadAsync(
            DownloadUrl(packageId, contentId),
            details.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid access session", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WhenPackageWasDeleted_Returns404()
    {
        var ownerId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(
            bytes: LargeItemBytes, userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);
        var details = await GetPackageDetailsAsync(packageId, userId: ownerId);

        var deleted = await DeletePackageAsync(packageId, userId: ownerId);
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);

        var response = await SendDownloadAsync(
            DownloadUrl(packageId, contentId),
            details.AccessSession.Token,
            userId: ownerId);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
        Assert.AreEqual(ProblemTypes.PackageGone, problem.Type);
    }

    [TestMethod]
    public async Task DownloadContent_WhenPackageHasExpired_Returns410()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);
        var details = await GetPackageDetailsAsync(packageId);

        // Expire the package while keeping its session valid: expiry is a hard
        // boundary even for an already-authorized download.
        await using (var connection = new NpgsqlConnection(Database.ConnectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE content_packages SET expires_at = @ExpiresAt WHERE id = @PackageId",
                new { ExpiresAt = TimeProvider.GetUtcNow().AddMinutes(-1), PackageId = packageId });
        }

        var response = await SendDownloadAsync(
            DownloadUrl(packageId, contentId),
            details.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package expired", problem.Title);
        Assert.AreEqual(ProblemTypes.PackageGone, problem.Type);
    }
}
