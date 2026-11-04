using System.Net;
using System.Text;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint-level integration tests for the content API, booting the real
/// application (permission wiring, authentication pipeline and real storage)
/// behind WebApplicationFactory.
/// </summary>
[TestClass]
public sealed class ContentEndpointTests : ShareItApiTestBase
{
    // ---------------------------------------------------------------
    // Permission wiring
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task GetUploadLimits_WithoutAnyIdentity_ReturnsConfiguredLimits()
    {
        // "Read.Limits" is granted to everyone, so even a fully anonymous
        // request must pass permission authorization and reach the endpoint.
        var response = await Client.GetAsync("/api/v1/content/package/limits");

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        var limits = await ReadJsonAsync<UploadLimitsResponse>(response);
        Assert.AreEqual(25L * 1024 * 1024, limits.AnonymousFileLimitBytes);
        Assert.AreEqual(100L * 1024 * 1024, limits.AuthenticatedFileLimitBytes);
    }

    [TestMethod]
    public async Task GetPackage_WithoutAnyIdentity_ReturnsEndpoint400InsteadOfAuthChallenge()
    {
        // "Read.Package" is granted to everyone as well: authorization must not
        // challenge (401), the endpoint itself rejects the missing identity.
        var response = await Client.GetAsync($"/api/v1/content/package/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithoutAnyIdentity_Returns400()
    {
        var response = await SendCreatePackageAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }

    // ---------------------------------------------------------------
    // Package creation
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task CreatePackage_AsGuest_Returns201WithLocationAndNoNames()
    {
        var guestId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        var response = await SendCreatePackageAsync(packageId: packageId, guestId: guestId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);

        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(packageId, package.Id);
        Assert.AreEqual(0, package.ItemCount);
        Assert.AreEqual(1, package.MaxDownloadCount);

        // Nothing names a package: neither title/description nor owner id are
        // part of the response anymore.
        Assert.IsFalse(body.Contains("Title", StringComparison.OrdinalIgnoreCase), body);
        Assert.IsFalse(body.Contains("Description", StringComparison.OrdinalIgnoreCase), body);
        Assert.IsFalse(body.Contains("UserId", StringComparison.OrdinalIgnoreCase), body);

        var location = response.Headers.Location?.ToString();
        Assert.IsNotNull(location);
        Assert.IsTrue(location.Contains("/api/v1/content/package/", StringComparison.Ordinal), location);
        Assert.IsTrue(location.Contains(package.Id.ToString(), StringComparison.Ordinal), location);
    }

    [TestMethod]
    public async Task CreatePackage_WithClientId_IsIdempotentForOwnerAndConflictsForOthers()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        var first = await SendCreatePackageAsync(packageId: packageId, userId: ownerId, maxDownloadCount: 3);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);

        var retry = await SendCreatePackageAsync(packageId: packageId, userId: ownerId, maxDownloadCount: 3);
        var retryBody = await retry.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, retryBody);
        Assert.AreEqual(packageId, (await ReadJsonAsync<ContentPackageResponse>(retry)).Id);

        var conflict = await SendCreatePackageAsync(packageId: packageId, userId: otherId, maxDownloadCount: 3);
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await ReadProblemAsync(conflict);
        Assert.AreEqual("Package id already in use", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithoutId_Returns400()
    {
        var response = await SendCreatePackageAsync(new CreatePackageRequest { MaxDownloadCount = 1 }, Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package id required", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithInvalidVerifier_Returns400()
    {
        var response = await SendCreatePackageAsync(
            guestId: Guid.NewGuid(),
            unlockVerifier: Verifier()[..20]);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid unlock verifier", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_PrivatePackage_AsGuest_Returns401()
    {
        var response = await SendCreatePackageAsync(isPrivate: true, guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Authentication required", problem.Title);
    }

    // ---------------------------------------------------------------
    // Upload + retrieval
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task UploadContent_AddsTextAndFileItemsWithKindAndOpaqueMeta()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var textMeta = MetaFor(null, "text/plain");
        var fileMeta = MetaFor("hello.txt", "text/plain");

        var textResponse = await SendUploadItemAsync(
            packageId, "text", Encoding.UTF8.GetBytes("some shared text"), textMeta, guestId: guestId);
        var textBody = await textResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, textResponse.StatusCode, textBody);

        var fileResponse = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, fileMeta, guestId: guestId);
        var fileBody = await fileResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, fileResponse.StatusCode, fileBody);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        Assert.HasCount(2, details.Items);
        Assert.AreEqual(2, details.Package.ItemCount);

        var text = details.Items.Single(item => item.Kind == "text");
        Assert.IsNotNull(text.Payload);
        Assert.AreEqual("some shared text", Encoding.UTF8.GetString(DecodePayload(text.Payload)));
        Assert.AreEqual(textMeta, text.Meta);
        Assert.IsNull(text.DownloadToken);

        var file = details.Items.Single(item => item.Kind == "file");
        Assert.IsNotNull(file.Payload);
        CollectionAssert.AreEqual(SmallItemBytes, DecodePayload(file.Payload));
        Assert.AreEqual(fileMeta, file.Meta);
        Assert.IsNull(file.DownloadToken);
    }

    [TestMethod]
    public async Task UploadContent_WithSameItemId_IsIdempotent()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();

        var first = await SendUploadItemAsync(packageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var retry = await SendUploadItemAsync(packageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);
        var retryBody = await retry.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, retryBody);
        Assert.AreEqual(1, (await ReadJsonAsync<ContentPackageResponse>(retry)).ItemCount);

        var counts = await GetDatabaseCountsAsync();
        Assert.AreEqual(1, counts.ContentCount);
    }

    [TestMethod]
    public async Task UploadContent_WithItemIdFromAnotherPackage_Returns409()
    {
        var guestId = Guid.NewGuid();
        var firstPackageId = await CreatePackageAsync(guestId: guestId);
        var secondPackageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();

        var first = await SendUploadItemAsync(firstPackageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var conflict = await SendUploadItemAsync(secondPackageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await ReadProblemAsync(conflict);
        Assert.AreEqual("Content id already in use", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_AsNonOwner_Returns403()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes, guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Forbidden", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_ForUnknownPackage_Returns404()
    {
        var response = await GetPackageAsync(Guid.NewGuid(), guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_PrivatePackage_VisibleOnlyToOwner()
    {
        var ownerId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            userId: ownerId, isPrivate: true, maxDownloadCount: 10, timeToLiveHours: 24);

        var guestResponse = await GetPackageAsync(packageId, guestId: Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.NotFound, guestResponse.StatusCode);

        var otherUserResponse = await GetPackageAsync(packageId, userId: Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.NotFound, otherUserResponse.StatusCode);

        var ownerResponse = await GetPackageAsync(packageId, userId: ownerId);
        Assert.AreEqual(HttpStatusCode.OK, ownerResponse.StatusCode);
    }

    // ---------------------------------------------------------------
    // Download flow (token generation + consumption)
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task DownloadContent_WithToken_ReturnsOpaqueBytes()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var fileItem = details.Items.Single();

        Assert.IsNull(fileItem.Payload);
        Assert.IsNotNull(fileItem.DownloadToken);
        Assert.IsNotNull(fileItem.DownloadUrl);
        Assert.IsNotNull(fileItem.TokenExpiresAt);

        var response = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        CollectionAssert.AreEqual(LargeItemBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.IsNull(response.Content.Headers.ContentDisposition, "The server must not learn or echo a file name.");
    }

    [TestMethod]
    public async Task DownloadContent_WithTokenForDifferentGuest_Returns400()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var fileItem = details.Items.Single();

        var response = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid download token", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithReusedToken_Returns400()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var fileItem = details.Items.Single();

        var first = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var second = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        Assert.AreEqual(HttpStatusCode.BadRequest, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Invalid download token", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithOneTokenInParallel_SucceedsExactlyOnce()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var fileItem = details.Items.Single();

        var responses = await Task.WhenAll(
            SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId),
            SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId));

        Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
    }

    [TestMethod]
    public async Task DownloadContent_WhenBlobIsMissing_KeepsTokenUsable()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var fileItem = details.Items.Single();

        var content = await Services.GetRequiredService<IContentStorageHandler>().GetContentAsync(contentId);
        Assert.IsNotNull(content);

        // Remove the blob, but remember its key so it can be restored.
        var blobKey = content.Url;
        await S3Client.DeleteObjectAsync(new Amazon.S3.Model.DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = blobKey
        });

        var failed = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        Assert.AreEqual(HttpStatusCode.NotFound, failed.StatusCode);

        // Put an object back under the same key: the token was not burned.
        await S3Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = BucketName,
            Key = blobKey,
            InputStream = new MemoryStream(LargeItemBytes)
        });

        var retried = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        var retriedBody = await retried.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retried.StatusCode, retriedBody);
    }

    // ---------------------------------------------------------------
    // Access limits
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task GetPackage_ConsumesOneAccessPerView_SecondViewReturns410()
    {
        // Opening the link consumes one access: with a limit of 1, the very
        // first view wins and every further view — even the same viewer
        // refreshing — is rejected.
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId, maxDownloadCount: 1);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var fileItem = details.Items.Single();

        var second = await GetPackageAsync(packageId, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.Gone, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Package access limit reached", problem.Title);

        // The download token issued on the winning view still works.
        var download = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        var downloadBody = await download.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode, downloadBody);

        // But the token is single-use.
        var downloadAgain = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        Assert.AreEqual(HttpStatusCode.BadRequest, downloadAgain.StatusCode);
    }

    [TestMethod]
    public async Task GetPackage_WithAccessLimit_AllowsExactlyMaxViews()
    {
        // Three distinct viewers may open the link; the fourth is rejected.
        // The limit is the creator's: guests are capped at 1, so an
        // authenticated owner sets the limit here.
        var (packageId, _) = await CreatePackageWithItemAsync(
            userId: Guid.NewGuid(), maxDownloadCount: 3, timeToLiveHours: 24);

        for (var access = 1; access <= 3; access++)
        {
            var response = await GetPackageAsync(packageId, guestId: Guid.NewGuid());
            var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        }

        var fourth = await GetPackageAsync(packageId, guestId: Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Gone, fourth.StatusCode);
        var problem = await ReadProblemAsync(fourth);
        Assert.AreEqual("Package access limit reached", problem.Title);
    }

    // ---------------------------------------------------------------
    // DELETE
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task DeletePackage_WithoutAnyIdentity_Returns401Challenge()
    {
        // "Delete.Package" is ForAuthenticatedUsers: an anonymous request must
        // be answered by the authorization pipeline with a plain 401 challenge.
        var response = await Client.DeleteAsync($"/api/v1/content/packages/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DeletePackage_AsGuest_Returns401EvenForOwnPackage()
    {
        // A guest owns the package but is not authenticated, so the
        // ForAuthenticatedUsers permission must still deny the request.
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await DeletePackageAsync(packageId, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DeletePackage_AsAuthenticatedNonOwner_Returns403()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(userId: ownerId, maxDownloadCount: 10, timeToLiveHours: 24);

        var response = await DeletePackageAsync(packageId, userId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Forbidden", problem.Title);
    }

    [TestMethod]
    public async Task DeletePackage_AsOwner_Returns204AndPackageBecomesUnreachable()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(userId: ownerId, maxDownloadCount: 10, timeToLiveHours: 24);

        var response = await DeletePackageAsync(packageId, userId: ownerId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode, body);

        var getResponse = await GetPackageAsync(packageId, userId: ownerId);
        Assert.AreEqual(HttpStatusCode.NotFound, getResponse.StatusCode);

        var storedContents = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(packageId, includeContent: true);
        Assert.IsNull(storedContents);
    }

    [TestMethod]
    public async Task DeletePackage_ForUnknownPackage_Returns404()
    {
        var response = await DeletePackageAsync(Guid.NewGuid(), userId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }
}
