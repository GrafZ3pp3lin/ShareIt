using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using kroet.ShareIt.Api.Content;
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
    public async Task GetPackage_WithoutAuthentication_IsAllowedAsGuest()
    {
        var response = await Client.GetAsync($"/api/v1/content/package/{Guid.NewGuid()}");

        // Guests are unauthenticated callers (user_id = null), not a separate
        // identity; an unknown package is a plain 404.
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithoutAuthentication_IsAllowedAsGuest()
    {
        var response = await SendCreatePackageAsync();

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var package = await ReadJsonAsync<ContentPackageOwnerResponse>(response);
        Assert.IsFalse(string.IsNullOrWhiteSpace(package.UploadToken));
    }

    // ---------------------------------------------------------------
    // Package creation
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task CreatePackage_AsGuest_Returns201WithLocationAndNoNames()
    {
        var packageId = Guid.NewGuid();

        var response = await SendCreatePackageAsync(packageId: packageId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);

        var package = await ReadJsonAsync<ContentPackageOwnerResponse>(response);
        Assert.AreEqual(packageId, package.Id);
        Assert.AreEqual(0, package.ItemCount);
        Assert.AreEqual(1, package.MaxAccessCount);
        Assert.IsNull(package.ShortCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(package.UploadToken));
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());

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
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        var response = await SendCreatePackageAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var span = activities.Single(activity => activity.OperationName == "package.create");
        Assert.AreEqual("guest", span.GetTagItem("shareit.identity.type"));
        Assert.AreEqual("created", span.GetTagItem("shareit.package.result"));
    }

    [TestMethod]
    public async Task CreatePackage_Retry_RequiresTheOwnerAndUploadToken()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        var first = await SendCreatePackageAsync(packageId: packageId, userId: ownerId, maxAccessCount: 3);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var firstPackage = await ReadJsonAsync<ContentPackageOwnerResponse>(first);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstPackage.UploadToken));

        var retry = await SendCreatePackageAsync(
            packageId: packageId,
            userId: ownerId,
            maxAccessCount: 3,
            uploadToken: firstPackage.UploadToken);
        var retryBody = await retry.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, retryBody);
        Assert.AreEqual("no-store", retry.Headers.CacheControl?.ToString());
        var retriedPackage = await ReadJsonAsync<ContentPackageOwnerResponse>(retry);
        Assert.AreEqual(packageId, retriedPackage.Id);
        Assert.AreEqual(firstPackage.UploadToken, retriedPackage.UploadToken);
        Assert.IsNull(firstPackage.ShortCode);
        Assert.IsNull(retriedPackage.ShortCode);

        var missingToken = await SendCreatePackageAsync(packageId: packageId, userId: ownerId, maxAccessCount: 3);
        Assert.AreEqual(HttpStatusCode.Conflict, missingToken.StatusCode);
        Assert.AreEqual("Package id already in use", (await ReadProblemAsync(missingToken)).Title);

        var wrongToken = await SendCreatePackageAsync(
            packageId: packageId,
            userId: ownerId,
            maxAccessCount: 3,
            uploadToken: "wrong-token");
        Assert.AreEqual(HttpStatusCode.Conflict, wrongToken.StatusCode);
        Assert.AreEqual("Package id already in use", (await ReadProblemAsync(wrongToken)).Title);

        var wrongOwner = await SendCreatePackageAsync(
            packageId: packageId,
            userId: otherId,
            maxAccessCount: 3,
            uploadToken: firstPackage.UploadToken);
        Assert.AreEqual(HttpStatusCode.Conflict, wrongOwner.StatusCode);
        Assert.AreEqual("Package id already in use", (await ReadProblemAsync(wrongOwner)).Title);
    }

    [TestMethod]
    public async Task CreatePackage_ConcurrentSameId_OneWinsAndTheOtherConflicts()
    {
        var packageId = Guid.NewGuid();

        var responses = await Task.WhenAll(
            SendCreatePackageAsync(packageId: packageId),
            SendCreatePackageAsync(packageId: packageId));

        var statuses = responses.Select(response => response.StatusCode).ToArray();
        Assert.AreEqual(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.AreEqual(1, statuses.Count(status => status == HttpStatusCode.Conflict));
    }

    [TestMethod]
    public async Task CreatePackage_WithoutId_Returns400()
    {
        var response = await SendCreatePackageAsync(new CreatePackageRequest { MaxAccessCount = 1, TimeToLiveHours = 1 });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package id required", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithInvalidVerifier_Returns400()
    {
        var response = await SendCreatePackageAsync(
            unlockVerifier: Verifier()[..20]);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid unlock verifier", problem.Title);
    }

    // ---------------------------------------------------------------
    // Short-code resolution
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task ResolveShortCode_ReturnsTheCanonicalPackageIdWithoutClaimingAnAccess()
    {
        var created = await CreateShortCodePackageAsync();
        await SendUploadItemAsync(created.Id, "file", SmallItemBytes);

        var response = await ResolveShortCodeAsync(created.ShortCode!.ToLowerInvariant());
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());

        var resolved = await ReadJsonAsync<ResolveShortCodeResponse>(response);
        Assert.AreEqual(created.Id, resolved.PackageId);

        // Resolution is only an alias lookup and must not consume the package's
        // single access; the open with the correct proof does.
        var details = await GetPackageDetailsAsync(created.Id, unlockProof: Verifier());
        Assert.AreEqual(1, details.Package.CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_DoesNotExposeTheOwnerShortCode()
    {
        var created = await CreateShortCodePackageAsync();
        await SendUploadItemAsync(created.Id, "file", SmallItemBytes);

        var response = await GetPackageAsync(created.Id, unlockProof: Verifier());
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        Assert.IsFalse(body.Contains("ShortCode", StringComparison.Ordinal), body);
    }

    [TestMethod]
    public async Task ResolveShortCode_NormalizesHyphensAndCase()
    {
        var created = await CreateShortCodePackageAsync();
        var code = created.ShortCode!;
        var formatted = $" {char.ToLowerInvariant(code[0])}-{char.ToLowerInvariant(code[1])}-{char.ToLowerInvariant(code[2])} ";

        var response = await ResolveShortCodeAsync(formatted);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual(created.Id, (await ReadJsonAsync<ResolveShortCodeResponse>(response)).PackageId);
    }

    [TestMethod]
    [DataRow("AB")]
    [DataRow("ABCD")]
    [DataRow("AB1")]
    [DataRow("NOTACODE")]
    public async Task ResolveShortCode_InvalidOrUnknown_Returns404(string code)
    {
        var response = await ResolveShortCodeAsync(code);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Short code not found", problem.Title);
    }

    [TestMethod]
    public async Task ResolveShortCode_ExpiredPackage_Returns404AndOpenReturns410()
    {
        var created = await CreateShortCodePackageAsync();
        await SendUploadItemAsync(created.Id, "file", SmallItemBytes);
        TimeProvider.Advance(ShortCode.PackageLifetime + TimeSpan.FromSeconds(1));

        var response = await ResolveShortCodeAsync(created.ShortCode!);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        var open = await GetPackageAsync(created.Id, unlockProof: Verifier());
        Assert.AreEqual(HttpStatusCode.Gone, open.StatusCode);
    }

    [TestMethod]
    public async Task ResolveShortCode_ExhaustedPackage_Returns404ButSessionStillWorks()
    {
        var created = await CreateShortCodePackageAsync();
        await SendUploadItemAsync(created.Id, "file", SmallItemBytes);

        var firstOpen = await GetPackageDetailsAsync(created.Id, unlockProof: Verifier());
        Assert.AreEqual(1, firstOpen.Package.CurrentAccessCount);

        var resolved = await ResolveShortCodeAsync(created.ShortCode!);
        Assert.AreEqual(HttpStatusCode.NotFound, resolved.StatusCode);

        var reOpen = await GetPackageAsync(
            created.Id,
            unlockProof: Verifier(),
            accessSession: firstOpen.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.OK, reOpen.StatusCode);
    }

    [TestMethod]
    public async Task GetPackage_AfterFiveFailedUnlockProofs_BurnsTheShortCodePackage()
    {
        var created = await CreateShortCodePackageAsync();
        await SendUploadItemAsync(created.Id, "file", SmallItemBytes);

        for (var attempt = 1; attempt <= ShortCode.MaxFailedUnlockAttempts; attempt++)
        {
            var failed = await GetPackageAsync(created.Id, unlockProof: Verifier(9));
            Assert.AreEqual(HttpStatusCode.Unauthorized, failed.StatusCode, $"Attempt {attempt}");
        }

        // The fifth miss expires the package atomically: the locator can no
        // longer be resolved and even the correct proof gets 410.
        var resolved = await ResolveShortCodeAsync(created.ShortCode!);
        Assert.AreEqual(HttpStatusCode.NotFound, resolved.StatusCode);

        var open = await GetPackageAsync(created.Id, unlockProof: Verifier());
        Assert.AreEqual(HttpStatusCode.Gone, open.StatusCode);
    }

    // ---------------------------------------------------------------
    // Upload + retrieval
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task UploadContent_AddsTextAndFileItemsWithKindAndOpaqueMeta()
    {
        var packageId = await CreatePackageAsync();

        var textMeta = MetaFor(null, "text/plain");
        var fileMeta = MetaFor("hello.txt", "text/plain");
        var textBytes = Encoding.UTF8.GetBytes("some shared text");

        var textResponse = await SendUploadItemAsync(packageId, "text", textBytes, textMeta);
        var textBody = await textResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, textResponse.StatusCode, textBody);

        var fileResponse = await SendUploadItemAsync(packageId, "file", SmallItemBytes, fileMeta);
        var fileBody = await fileResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, fileResponse.StatusCode, fileBody);

        var details = await GetPackageDetailsAsync(packageId);

        Assert.HasCount(2, details.Items);
        Assert.AreEqual(2, details.Package.ItemCount);

        var text = details.Items.Single(item => item.Kind == "text");
        Assert.AreEqual(textMeta, text.Meta);

        var file = details.Items.Single(item => item.Kind == "file");
        Assert.AreEqual(fileMeta, file.Meta);

        // Nothing is inlined: every item's bytes come through the access session.
        var textDownload = await SendDownloadAsync(DownloadUrl(packageId, text.Id), details.AccessSession.Token);
        var textDownloadBody = await textDownload.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, textDownload.StatusCode, textDownloadBody);
        Assert.AreSequenceEqual(textBytes, await textDownload.Content.ReadAsByteArrayAsync());

        var fileDownload = await SendDownloadAsync(DownloadUrl(packageId, file.Id), details.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.OK, fileDownload.StatusCode);
        Assert.AreSequenceEqual(SmallItemBytes, await fileDownload.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task UploadContent_WithSameItemId_IsIdempotent()
    {
        var packageId = await CreatePackageAsync();
        var contentId = Guid.NewGuid();

        var first = await SendUploadItemAsync(packageId, "file", SmallItemBytes, contentId: contentId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var retry = await SendUploadItemAsync(packageId, "file", SmallItemBytes, contentId: contentId);
        var retryBody = await retry.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, retryBody);
        Assert.AreEqual(1, (await ReadJsonAsync<ContentPackageOwnerResponse>(retry)).ItemCount);

        var (_, contentCount) = await GetDatabaseCountsAsync();
        Assert.AreEqual(1, contentCount);
    }

    [TestMethod]
    public async Task UploadContent_WithItemIdFromAnotherPackage_Returns409()
    {
        var firstPackageId = await CreatePackageAsync();
        var secondPackageId = await CreatePackageAsync();
        var contentId = Guid.NewGuid();

        var first = await SendUploadItemAsync(firstPackageId, "file", SmallItemBytes, contentId: contentId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var conflict = await SendUploadItemAsync(secondPackageId, "file", SmallItemBytes, contentId: contentId);
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await ReadProblemAsync(conflict);
        Assert.AreEqual("Content id already in use", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithWrongUploadToken_Returns403()
    {
        var packageId = await CreatePackageAsync();

        // Well-formed but not this package's: a shape error is a 400, so this
        // must reach the owner check to exercise the 403.
        var response = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, uploadToken: OpaqueToken.Create());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Forbidden", problem.Title);
    }

    [TestMethod]
    public async Task UploadContent_WithMalformedUploadToken_Returns400()
    {
        var packageId = await CreatePackageAsync();

        var response = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, uploadToken: "not-a-valid-upload-token");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid upload token", problem.Title);
        Assert.Contains(
            $"{OpaqueToken.WellFormedLength} base64url characters",
            problem.Detail ?? string.Empty);
    }

    [TestMethod]
    public async Task UploadContent_AsAuthenticatedNonOwner_Returns403()
    {
        var packageId = await CreatePackageAsync();

        // The user id does not match the guest-owned package, even with the
        // package's upload token.
        var response = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, userId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Forbidden", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_ForUnknownPackage_Returns404()
    {
        var response = await GetPackageAsync(Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package not found", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_ResponseIsNotCacheable()
    {
        var (packageId, _) = await CreatePackageWithItemAsync();

        var response = await GetPackageAsync(packageId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
    }

    // ---------------------------------------------------------------
    // Download flow (access session)
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task DownloadContent_WithAccessSession_ReturnsOpaqueBytes()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);

        var details = await GetPackageDetailsAsync(packageId);
        Assert.IsFalse(string.IsNullOrEmpty(details.AccessSession.Token));

        var response = await SendDownloadAsync(DownloadUrl(packageId, contentId), details.AccessSession.Token);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        Assert.AreSequenceEqual(LargeItemBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
        Assert.IsNull(response.Content.Headers.ContentDisposition, "The server must not learn or echo a file name.");
    }

    [TestMethod]
    public async Task DownloadContent_WithSessionForDifferentIdentity_Returns401()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);

        var details = await GetPackageDetailsAsync(packageId);

        var response = await SendDownloadAsync(
            DownloadUrl(packageId, contentId),
            details.AccessSession.Token,
            userId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid access session", problem.Title);
    }

    [TestMethod]
    public async Task DownloadContent_WithReusedSession_SucceedsRepeatedly()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);

        var details = await GetPackageDetailsAsync(packageId);

        var first = await SendDownloadAsync(DownloadUrl(packageId, contentId), details.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var second = await SendDownloadAsync(DownloadUrl(packageId, contentId), details.AccessSession.Token);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode, secondBody);
    }

    [TestMethod]
    public async Task DownloadContent_WithSessionInParallel_BothSucceed()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);

        var details = await GetPackageDetailsAsync(packageId);
        var downloadUrl = DownloadUrl(packageId, contentId);

        var responses = await Task.WhenAll(
            SendDownloadAsync(downloadUrl, details.AccessSession.Token),
            SendDownloadAsync(downloadUrl, details.AccessSession.Token));

        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK));
    }

    [TestMethod]
    public async Task DownloadContent_WhenBlobIsMissing_KeepsSessionUsable()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);

        var details = await GetPackageDetailsAsync(packageId);

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
        var failed = await SendDownloadAsync(downloadUrl, details.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.NotFound, failed.StatusCode);
        Assert.AreEqual(0L, metrics.Sum("shareit.bytes.authorized"));

        // Put an object back under the same key: the session was not burned.
        await S3Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = BucketName,
            Key = blobKey,
            InputStream = new MemoryStream(LargeItemBytes)
        });

        var retried = await SendDownloadAsync(downloadUrl, details.AccessSession.Token);
        var retriedBody = await retried.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retried.StatusCode, retriedBody);
        Assert.AreEqual((long)LargeItemBytes.Length, metrics.Sum("shareit.bytes.authorized"));
    }

    [TestMethod]
    public async Task DownloadContent_WithoutSession_Returns400()
    {
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes);

        var response = await SendDownloadAsync(DownloadUrl(packageId, contentId), null);

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
        var (packageId, contentId) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, maxAccessCount: 1);

        var details = await GetPackageDetailsAsync(packageId);

        var second = await GetPackageAsync(packageId);
        Assert.AreEqual(HttpStatusCode.Gone, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Package access limit reached", problem.Title);

        var download = await SendDownloadAsync(
            DownloadUrl(packageId, contentId), details.AccessSession.Token);
        var downloadBody = await download.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode, downloadBody);

        var downloadAgain = await SendDownloadAsync(
            DownloadUrl(packageId, contentId), details.AccessSession.Token);
        Assert.AreEqual(HttpStatusCode.OK, downloadAgain.StatusCode);
    }

    [TestMethod]
    public async Task GetPackage_WithItsSession_ReusesTheAccess()
    {
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: LargeItemBytes, maxAccessCount: 1);

        var first = await GetPackageDetailsAsync(packageId);
        Assert.AreEqual(1, first.Package.CurrentAccessCount);

        var second = await GetPackageDetailsAsync(packageId, accessSession: first.AccessSession.Token);

        Assert.AreEqual(first.AccessSession.Token, second.AccessSession.Token);
        Assert.AreEqual(1, second.Package.CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithAccessLimit_AllowsExactlyMaxViews()
    {
        // Three opens without a session each claim an access; the fourth is
        // rejected. The limit is the creator's: guests are capped at 1, so an
        // authenticated owner sets the limit here.
        var (packageId, _) = await CreatePackageWithItemAsync(
            userId: Guid.NewGuid(), maxAccessCount: 3, timeToLiveHours: 24);

        for (var access = 1; access <= 3; access++)
        {
            var response = await GetPackageAsync(packageId);
            var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        }

        var fourth = await GetPackageAsync(packageId);
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
        var packageId = await CreatePackageAsync();

        var response = await DeletePackageAsync(packageId);

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
