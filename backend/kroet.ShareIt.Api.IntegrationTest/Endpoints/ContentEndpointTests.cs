using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint-level integration tests for the content API, booting the real
/// application (authentication pipeline and real storage) behind
/// WebApplicationFactory.
/// </summary>
[TestClass]
public sealed class ContentEndpointTests : ShareItApiTestBase
{
    // ---------------------------------------------------------------
    // Open endpoints without identity
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task GetUploadLimits_WithoutAnyIdentity_ReturnsConfiguredLimits()
    {
        var response = await Client.GetAsync("/api/v1/content/package/limits");

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        var limits = await ReadJsonAsync<UploadLimitsResponse>(response);
        Assert.AreEqual(25L * 1024 * 1024, limits.AnonymousFileLimitBytes);
        Assert.AreEqual(100L * 1024 * 1024, limits.AuthenticatedFileLimitBytes);
        Assert.AreEqual(100, limits.MaxItemsPerPackage);
    }

    [TestMethod]
    public async Task GetPackage_WithoutAnyIdentity_ReturnsEndpoint400InsteadOfAuthChallenge()
    {
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
        Assert.AreEqual(1, package.MaxAccessCount);
        Assert.IsNotNull(package.ExpiresAt);

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
    public async Task CreatePackage_AsGuest_EmitsPackageCreateSpan()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ShareItActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var span = activities.Single(activity => activity.OperationName == "package.create");
        Assert.AreEqual("guest", span.GetTagItem("shareit.identity.type"));
        Assert.AreEqual("created", span.GetTagItem("shareit.package.result"));
    }

    [TestMethod]
    public async Task CreatePackage_WithClientId_IsIdempotentForOwnerAndConflictsForOthers()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        var first = await SendCreatePackageAsync(packageId: packageId, userId: ownerId, maxAccessCount: 3);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);

        var retry = await SendCreatePackageAsync(packageId: packageId, userId: ownerId, maxAccessCount: 3);
        var retryBody = await retry.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, retryBody);
        Assert.AreEqual(packageId, (await ReadJsonAsync<ContentPackageResponse>(retry)).Id);

        var conflict = await SendCreatePackageAsync(packageId: packageId, userId: otherId, maxAccessCount: 3);
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await ReadProblemAsync(conflict);
        Assert.AreEqual("Package id already in use", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithoutId_Returns400()
    {
        var response = await SendCreatePackageAsync(new CreatePackageRequest { MaxAccessCount = 1, TimeToLiveHours = 1 }, Guid.NewGuid());

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
        var textBytes = Encoding.UTF8.GetBytes("some shared text");

        var textResponse = await SendUploadItemAsync(packageId, "text", textBytes, textMeta, guestId: guestId);
        var textBody = await textResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, textResponse.StatusCode, textBody);

        var fileResponse = await SendUploadItemAsync(packageId, "file", SmallItemBytes, fileMeta, guestId: guestId);
        var fileBody = await fileResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, fileResponse.StatusCode, fileBody);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        Assert.HasCount(2, details.Items);
        Assert.AreEqual(2, details.Package.ItemCount);

        var text = details.Items.Single(item => item.Kind == "text");
        Assert.AreEqual(textMeta, text.Meta);

        var file = details.Items.Single(item => item.Kind == "file");
        Assert.AreEqual(fileMeta, file.Meta);

        // Nothing is inlined: every item's bytes come through the access session.
        var textDownload = await SendDownloadAsync(DownloadUrl(packageId, text.Id), details.AccessSession.Token, guestId);
        var textDownloadBody = await textDownload.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, textDownload.StatusCode, textDownloadBody);
        CollectionAssert.AreEqual(textBytes, await textDownload.Content.ReadAsByteArrayAsync());

        var fileDownload = await SendDownloadAsync(DownloadUrl(packageId, file.Id), details.AccessSession.Token, guestId);
        Assert.AreEqual(HttpStatusCode.OK, fileDownload.StatusCode);
        CollectionAssert.AreEqual(SmallItemBytes, await fileDownload.Content.ReadAsByteArrayAsync());
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
    public async Task GetPackage_ResponseIsNotCacheable()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(guestId: guestId);

        var response = await GetPackageAsync(packageId, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
    }

    // ---------------------------------------------------------------
    // Download flow (access session)
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task DownloadContent_WithAccessSession_ReturnsOpaqueBytes()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        Assert.IsFalse(string.IsNullOrEmpty(details.AccessSession.Token));

        var response = await SendDownloadAsync(DownloadUrl(packageId, contentId), details.AccessSession.Token, guestId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        CollectionAssert.AreEqual(LargeItemBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
        Assert.IsNull(response.Content.Headers.ContentDisposition, "The server must not learn or echo a file name.");
    }

    [TestMethod]
    public async Task DownloadContent_WithSessionForDifferentGuest_Returns401()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        var response = await SendDownloadAsync(
            DownloadUrl(packageId, contentId),
            details.AccessSession.Token,
            Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid access session", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithReusedSession_SucceedsRepeatedly()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        var first = await SendDownloadAsync(DownloadUrl(packageId, contentId), details.AccessSession.Token, guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var second = await SendDownloadAsync(DownloadUrl(packageId, contentId), details.AccessSession.Token, guestId);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode, secondBody);
    }

    [TestMethod]
    public async Task DownloadContent_WithSessionInParallel_BothSucceed()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var downloadUrl = DownloadUrl(packageId, contentId);

        var responses = await Task.WhenAll(
            SendDownloadAsync(downloadUrl, details.AccessSession.Token, guestId),
            SendDownloadAsync(downloadUrl, details.AccessSession.Token, guestId));

        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK));
    }

    [TestMethod]
    public async Task DownloadContent_WhenBlobIsMissing_KeepsSessionUsable()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        var content = await Services.GetRequiredService<IContentStorageHandler>().GetContentAsync(contentId);
        Assert.IsNotNull(content);

        // Remove the blob, but remember its key so it can be restored.
        var blobKey = content.Url;
        await S3Client.DeleteObjectAsync(new Amazon.S3.Model.DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = blobKey
        });

        using var metrics = new MetricsCapture();

        var downloadUrl = DownloadUrl(packageId, contentId);
        var failed = await SendDownloadAsync(downloadUrl, details.AccessSession.Token, guestId);
        Assert.AreEqual(HttpStatusCode.NotFound, failed.StatusCode);
        Assert.AreEqual(0L, metrics.Sum("shareit.bytes.authorized"));

        // Put an object back under the same key: the session was not burned.
        await S3Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = BucketName,
            Key = blobKey,
            InputStream = new MemoryStream(LargeItemBytes)
        });

        var retried = await SendDownloadAsync(downloadUrl, details.AccessSession.Token, guestId);
        var retriedBody = await retried.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retried.StatusCode, retriedBody);
        Assert.AreEqual((long)LargeItemBytes.Length, metrics.Sum("shareit.bytes.authorized"));
    }

    [TestMethod]
    public async Task DownloadContent_WithoutSession_Returns400()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId);

        var response = await SendDownloadAsync(DownloadUrl(packageId, contentId), null, guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Missing access session", problem.Title);
    }

    // ---------------------------------------------------------------
    // Access limits
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task GetPackage_ConsumesOneAccessPerView_SecondViewWithoutSessionReturns410()
    {
        // Opening the link consumes one access: with a limit of 1, the very
        // first view wins and every further view without its session is
        // rejected. Downloads within the session stay available.
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId, maxAccessCount: 1);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        var second = await GetPackageAsync(packageId, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.Gone, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Package access limit reached", problem.Title);

        var download = await SendDownloadAsync(
            DownloadUrl(packageId, contentId), details.AccessSession.Token, guestId);
        var downloadBody = await download.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode, downloadBody);

        var downloadAgain = await SendDownloadAsync(
            DownloadUrl(packageId, contentId), details.AccessSession.Token, guestId);
        Assert.AreEqual(HttpStatusCode.OK, downloadAgain.StatusCode);
    }

    [TestMethod]
    public async Task GetPackage_WithItsSession_ReusesTheAccess()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, guestId: guestId, maxAccessCount: 1);

        var first = await GetPackageDetailsAsync(packageId, guestId: guestId);
        Assert.AreEqual(1, first.Package.CurrentAccessCount);

        var second = await GetPackageDetailsAsync(packageId, guestId: guestId, accessSession: first.AccessSession.Token);

        Assert.AreEqual(first.AccessSession.Token, second.AccessSession.Token);
        Assert.AreEqual(1, second.Package.CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithAccessLimit_AllowsExactlyMaxViews()
    {
        // Three distinct viewers may open the link; the fourth is rejected.
        // The limit is the creator's: guests are capped at 1, so an
        // authenticated owner sets the limit here.
        var (packageId, _) = await CreatePackageWithItemAsync(
            userId: Guid.NewGuid(), maxAccessCount: 3, timeToLiveHours: 24);

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
        // /packages requires authorization: an anonymous request must be
        // answered by the authorization pipeline with a plain 401.
        var response = await Client.DeleteAsync($"/api/v1/content/packages/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DeletePackage_AsGuest_Returns401EvenForOwnPackage()
    {
        // A guest owns the package but is not authenticated, so authorization
        // must still deny the request.
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await DeletePackageAsync(packageId, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DeletePackage_AsAuthenticatedNonOwner_Returns403()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);

        var response = await DeletePackageAsync(packageId, userId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Forbidden", problem.Title);
    }

    [TestMethod]
    public async Task DeletePackage_AsOwner_Returns204AndPackageBecomesUnreachable()
    {
        var ownerId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);
        Assert.AreEqual(1, await CountStoredObjectsAsync(), "The item must be stored before deletion.");

        var response = await DeletePackageAsync(packageId, userId: ownerId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode, body);

        var getResponse = await GetPackageAsync(packageId, userId: ownerId);
        Assert.AreEqual(HttpStatusCode.NotFound, getResponse.StatusCode);

        var storedContents = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(packageId, includeContent: true);
        Assert.IsNull(storedContents);

        // Owner deletion only queues the blobs; cleanup performs the physical
        // deletion without making the DELETE request wait for S3.
        Assert.AreEqual(1, await CountStoredObjectsAsync(), "Owner deletion leaves the queued object in S3.");
        var contentHandler = Services.GetRequiredService<IContentStorageHandler>();
        Assert.HasCount(
            1,
            await contentHandler.GetPendingBlobDeletionsAsync(10),
            "Owner deletion must queue the blobs for cleanup.");

        await RunCleanupAsync();

        Assert.AreEqual(0, await CountStoredObjectsAsync(), "Cleanup must delete the queued blobs.");
        Assert.IsEmpty(
            await contentHandler.GetPendingBlobDeletionsAsync(10),
            "Cleanup must drain the blob-deletion queue.");
    }

    [TestMethod]
    public async Task DeletePackage_ForUnknownPackage_Returns404()
    {
        var response = await DeletePackageAsync(Guid.NewGuid(), userId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    private sealed class MetricsCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, long Value)> _measurements = [];

        public MetricsCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ShareItMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
                _measurements.Add((instrument.Name, value)));
            _listener.Start();
        }

        public int Count(string name)
            => _measurements.Count(measurement => measurement.Name == name);

        public long Sum(string name)
            => _measurements.Where(measurement => measurement.Name == name).Sum(measurement => measurement.Value);

        public void Dispose()
            => _listener.Dispose();
    }
}
