using System.Net;
using System.Text;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint-level integration tests for the content API, booting the real
/// application (permission wiring, authentication pipeline and in-memory
/// storage) behind WebApplicationFactory.
/// </summary>
[TestClass]
public sealed class ContentEndpointTests : ShareItApiTestBase
{
    private static readonly byte[] FileBytes = "hello world"u8.ToArray();

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
        var response = await SendCreatePackageAsync(fileBytes: FileBytes);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }

    // ---------------------------------------------------------------
    // Package creation and retrieval
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task CreatePackage_AsGuest_WithFile_Returns201WithLocationAndOwner()
    {
        var guestId = Guid.NewGuid();

        var response = await SendCreatePackageAsync(fileBytes: FileBytes, title: "My title", guestId: guestId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);

        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual("My title", package.Title);
        Assert.AreEqual(guestId, package.UserId);
        Assert.AreEqual(1, package.ItemCount);

        var location = response.Headers.Location?.ToString();
        Assert.IsNotNull(location);
        Assert.IsTrue(location.Contains("/api/v1/content/package/", StringComparison.Ordinal), location);
        Assert.IsTrue(location.Contains(package.Id.ToString(), StringComparison.Ordinal), location);
    }

    [TestMethod]
    public async Task GetPackage_AsGuest_ReturnsTextAndFileItems()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var textResponse = await SendAddTextAsync(packageId, "some shared text", guestId: guestId);
        var textBody = await textResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, textResponse.StatusCode, textBody);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        Assert.HasCount(2, details.Items);

        var text = details.Items.Single(item => item.Type == "text");
        Assert.AreEqual("some shared text", text.Text);
        Assert.AreEqual("text/plain", text.MimeType);

        var file = details.Items.Single(item => item.Type == "file");
        Assert.AreEqual("hello.txt", file.FileName);
        Assert.IsFalse(string.IsNullOrEmpty(file.DownloadToken));
        Assert.IsFalse(string.IsNullOrEmpty(file.DownloadUrl));
        Assert.IsNotNull(file.TokenExpiresAt);
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
    public async Task GetPackage_ConsumesOneAccessPerView_SecondViewReturns410()
    {
        // Opening the link consumes one access: with a limit of 1, the very
        // first view wins and every further view — even the same viewer
        // refreshing — is rejected.
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes, maxDownloadCount: 1);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var fileItem = details.Items.Single(item => item.Type == "file");

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
        var packageId = await CreatePackageAsync(guestId: Guid.NewGuid(), fileBytes: FileBytes, maxDownloadCount: 3);

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

    [TestMethod]
    public async Task CreatePackage_PrivatePackage_AsGuest_Returns401()
    {
        var response = await SendCreatePackageAsync(fileBytes: FileBytes, isPrivate: true, guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Authentication required", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_PrivatePackage_VisibleOnlyToOwner()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(fileBytes: FileBytes, isPrivate: true, userId: ownerId);

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
    public async Task DownloadContent_WithToken_ReturnsFileContent()
    {
        var guestId = Guid.NewGuid();
        var (_, fileItem) = await CreateGuestFilePackageAsync(guestId);

        var request = new HttpRequestMessage(HttpMethod.Get, fileItem.DownloadUrl);
        request.Headers.Add("X-File-Token", fileItem.DownloadToken);
        request.Headers.Add("X-Guest-ID", guestId.ToString());

        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        CollectionAssert.AreEqual(FileBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.IsNotNull(response.Content.Headers.ContentDisposition);
        Assert.AreEqual("hello.txt", response.Content.Headers.ContentDisposition.FileName);
    }

    [TestMethod]
    public async Task DownloadContent_WithTokenForDifferentGuest_Returns400()
    {
        var guestId = Guid.NewGuid();
        var (_, fileItem) = await CreateGuestFilePackageAsync(guestId);

        var request = new HttpRequestMessage(HttpMethod.Get, fileItem.DownloadUrl);
        request.Headers.Add("X-File-Token", fileItem.DownloadToken);
        request.Headers.Add("X-Guest-ID", Guid.NewGuid().ToString());

        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid download token", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithReusedToken_Returns400()
    {
        var guestId = Guid.NewGuid();
        var (_, fileItem) = await CreateGuestFilePackageAsync(guestId);

        var first = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var second = await SendDownloadAsync(fileItem.DownloadUrl!, fileItem.DownloadToken!, guestId);
        Assert.AreEqual(HttpStatusCode.BadRequest, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Invalid download token", problem.Title);
    }

    // ---------------------------------------------------------------
    // Upload ownership
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task UploadFilesToPackage_AsNonOwner_Returns403()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var response = await SendUploadFilesAsync(packageId, [1, 2, 3], guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Forbidden", problem.Title);
    }

    [TestMethod]
    public async Task AddTextToPackage_AsNonOwner_Returns403()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var response = await SendAddTextAsync(packageId, "intrusion", guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Forbidden", problem.Title);
    }

    // ---------------------------------------------------------------
    // DELETE permission wiring (401 / 403)
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
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var response = await DeletePackageAsync(packageId, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DeletePackage_AsAuthenticatedNonOwner_Returns403()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(userId: ownerId, fileBytes: FileBytes);

        var response = await DeletePackageAsync(packageId, userId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Forbidden", problem.Title);
    }

    [TestMethod]
    public async Task DeletePackage_AsOwner_Returns204AndPackageBecomesUnreachable()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(userId: ownerId, fileBytes: FileBytes);

        var response = await DeletePackageAsync(packageId, userId: ownerId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode, body);

        var getResponse = await GetPackageAsync(packageId, userId: ownerId);
        Assert.AreEqual(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [TestMethod]
    public async Task DeletePackage_ForUnknownPackage_Returns404()
    {
        var response = await DeletePackageAsync(Guid.NewGuid(), userId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a guest-owned package with one file and fetches it, returning
    /// the file item with its download token.
    /// </summary>
    private async Task<(Guid PackageId, ContentItemInPackage FileItem)> CreateGuestFilePackageAsync(Guid guestId)
    {
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);
        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        var fileItem = details.Items.Single(item => item.Type == "file");
        return (packageId, fileItem);
    }
}
