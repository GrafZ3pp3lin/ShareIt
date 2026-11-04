using System.Net;
using System.Text.Json;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint tests for the owner-only package preview: basic information
/// without content or tokens, never consuming an access. Consumers (guests)
/// must never be able to use it.
/// </summary>
[TestClass]
public sealed class PackagePreviewTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task GetPackagePreview_AsGuest_Returns401Challenge()
    {
        // "Preview.Package" is ForAuthenticatedUsers: guests (consumers) are
        // rejected by the authorization pipeline with a plain 401 challenge.
        var ownerId = Guid.NewGuid();
        var packageId = (await CreatePackageWithItemAsync(
            userId: ownerId, bytes: "hello"u8.ToArray(), maxDownloadCount: 10, timeToLiveHours: 24)).PackageId;

        var request = PreviewRequest(packageId, ownerId, isGuest: true);
        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetPackagePreview_AsAuthenticatedNonOwner_Returns404()
    {
        // The preview must not reveal other users' packages.
        var ownerId = Guid.NewGuid();
        var packageId = (await CreatePackageWithItemAsync(
            userId: ownerId, bytes: "hello"u8.ToArray(), maxDownloadCount: 10, timeToLiveHours: 24)).PackageId;

        var request = PreviewRequest(packageId, Guid.NewGuid(), isGuest: false);
        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    [TestMethod]
    public async Task GetPackagePreview_AsOwner_ReturnsBasicInfoWithoutContent()
    {
        var ownerId = Guid.NewGuid();
        var packageId = (await CreatePackageWithItemAsync(
            userId: ownerId, bytes: "hello"u8.ToArray(), maxDownloadCount: 3, timeToLiveHours: 24)).PackageId;

        var request = PreviewRequest(packageId, ownerId, isGuest: false);
        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.AreEqual(packageId, root.GetProperty("Id").GetGuid());
        Assert.AreEqual(1, root.GetProperty("ItemCount").GetInt32());
        Assert.AreEqual(3, root.GetProperty("MaxDownloadCount").GetInt32());
        Assert.AreEqual(0, root.GetProperty("CurrentDownloadCount").GetInt32());
        Assert.AreEqual(3, root.GetProperty("RemainingDownloads").GetInt32());
        Assert.IsFalse(root.GetProperty("IsExpired").GetBoolean());

        // No content may leak: no items, no tokens, no inline text.
        Assert.IsFalse(root.TryGetProperty("Items", out _), body);
        Assert.IsFalse(body.Contains("DownloadToken", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains("hello", StringComparison.Ordinal), body);
    }

    [TestMethod]
    public async Task GetPackagePreview_IsRepeatable_AndDoesNotConsumeAccesses()
    {
        var ownerId = Guid.NewGuid();
        var packageId = (await CreatePackageWithItemAsync(
            userId: ownerId, bytes: "hello"u8.ToArray(), maxDownloadCount: 1, timeToLiveHours: 24)).PackageId;

        for (var call = 0; call < 3; call++)
        {
            var request = PreviewRequest(packageId, ownerId, isGuest: false);
            var response = await Client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        // The access is untouched: the consumer link still works.
        var view = await GetPackageAsync(packageId, guestId: Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.OK, view.StatusCode);
    }

    [TestMethod]
    public async Task GetPackagePreview_ForUnknownPackage_Returns404()
    {
        var request = PreviewRequest(Guid.NewGuid(), Guid.NewGuid(), isGuest: false);
        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    private static HttpRequestMessage PreviewRequest(Guid packageId, Guid userId, bool isGuest)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/content/package/{packageId}/preview");
        if (isGuest)
        {
            request.Headers.Add("X-Guest-ID", userId.ToString());
        }
        else
        {
            request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        }

        return request;
    }
}
