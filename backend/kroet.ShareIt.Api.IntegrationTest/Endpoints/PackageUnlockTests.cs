using System.Net;
using Amazon.S3.Model;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Proof-gated reads: a protected package reveals nothing and consumes
/// nothing until a valid <c>X-Unlock-Proof</c> arrives.
/// </summary>
[TestClass]
public sealed class PackageUnlockTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task GetPackage_WithoutProof_Returns401AndKeepsTheAccessCount()
    {
        var guestId = Guid.NewGuid();
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxAccessCount: 1, unlockVerifier: verifier);

        var response = await GetPackageAsync(packageId, guestId: guestId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, body);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Unlock required", problem.Title);

        // No metadata, no items, no names, no proof echo.
        Assert.IsFalse(body.Contains("Items", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains("AccessSession", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains(verifier, StringComparison.Ordinal), body);

        var shell = await GetShellAsync(packageId);
        Assert.AreEqual(0, shell.CurrentAccessCount, "A rejected unlock must not consume an access.");
    }

    [TestMethod]
    public async Task GetPackage_WithWrongProof_Returns401AndKeepsTheAccessCount()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxAccessCount: 1, unlockVerifier: Verifier(1));

        var response = await GetPackageAsync(packageId, guestId: guestId, unlockProof: Verifier(2));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, (await GetShellAsync(packageId)).CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithMalformedProof_Returns401()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxAccessCount: 1, unlockVerifier: Verifier());

        var response = await GetPackageAsync(packageId, guestId: guestId, unlockProof: "not-base64url!");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, (await GetShellAsync(packageId)).CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithCorrectProof_ReturnsContentAndConsumesOneAccess()
    {
        var guestId = Guid.NewGuid();
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxAccessCount: 1, unlockVerifier: verifier);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId, unlockProof: verifier);

        Assert.HasCount(1, details.Items);
        Assert.AreEqual(1, details.Package.CurrentAccessCount);
        Assert.AreEqual(1, (await GetShellAsync(packageId)).CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_ReportsWhetherThePackageIsProtected()
    {
        var guestId = Guid.NewGuid();
        var (openPackageId, _) = await CreatePackageWithItemAsync(guestId: guestId);

        var open = await GetPackageDetailsAsync(openPackageId, guestId: guestId);
        Assert.IsFalse(open.Package.IsProtected);

        var verifier = Verifier();
        var (protectedPackageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, unlockVerifier: verifier);

        var locked = await GetPackageDetailsAsync(
            protectedPackageId, guestId: guestId, unlockProof: verifier);
        Assert.IsTrue(locked.Package.IsProtected);
    }

    [TestMethod]
    public async Task GetPackage_WithSessionAndProof_ReusesTheAccess()
    {
        var guestId = Guid.NewGuid();
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxAccessCount: 1, unlockVerifier: verifier);

        var first = await GetPackageDetailsAsync(packageId, guestId: guestId, unlockProof: verifier);
        var second = await GetPackageDetailsAsync(
            packageId,
            guestId: guestId,
            unlockProof: verifier,
            accessSession: first.AccessSession.Token);

        Assert.AreEqual(first.AccessSession.Token, second.AccessSession.Token);
        Assert.AreEqual(1, second.Package.CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithSessionButWithoutProof_Returns401AndKeepsTheAccessCount()
    {
        var guestId = Guid.NewGuid();
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxAccessCount: 1, unlockVerifier: verifier);

        var first = await GetPackageDetailsAsync(packageId, guestId: guestId, unlockProof: verifier);

        var response = await GetPackageAsync(
            packageId,
            guestId: guestId,
            accessSession: first.AccessSession.Token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, body);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Unlock required", problem.Title);
        Assert.IsFalse(body.Contains("Items", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains("AccessSession", StringComparison.Ordinal), body);
        Assert.AreEqual(1, (await GetShellAsync(packageId)).CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithCorrectProofAtAccessLimit_Returns410OnSecondRead()
    {
        var guestId = Guid.NewGuid();
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxAccessCount: 1, unlockVerifier: verifier);

        await GetPackageDetailsAsync(packageId, guestId: guestId, unlockProof: verifier);

        var second = await GetPackageAsync(packageId, guestId: guestId, unlockProof: verifier);
        Assert.AreEqual(HttpStatusCode.Gone, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Package access limit reached", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_WhenExhaustedPackageHasMissingBlob_Returns410WithoutReadingIt()
    {
        // An open performs zero blob reads, so a missing object must not turn
        // a second, rejected read of an exhausted package into a 500.
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(
            bytes: SmallItemBytes, guestId: guestId, maxAccessCount: 1);

        await GetPackageDetailsAsync(packageId, guestId: guestId);

        var content = await Services.GetRequiredService<IContentStorageHandler>()
            .GetContentAsync(contentId);
        Assert.IsNotNull(content);
        await S3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = content.Url
        });

        var response = await GetPackageAsync(packageId, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package access limit reached", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_WhenProtectedPackageIsExpired_Returns410WithoutProof()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxAccessCount: 1, unlockVerifier: Verifier());

        TimeProvider.SetUtcNow(TimeProvider.GetUtcNow().AddHours(2));

        var response = await GetPackageAsync(packageId, guestId: guestId);

        // Expiry is not content, so 410 may be answered before the proof.
        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package expired", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_ForUnprotectedPackage_NeedsNoProof()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(guestId: guestId);

        var response = await GetPackageAsync(packageId, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<ContentPackage> GetShellAsync(Guid packageId)
    {
        var shell = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageShellAsync(packageId);
        Assert.IsNotNull(shell);
        return shell;
    }
}
