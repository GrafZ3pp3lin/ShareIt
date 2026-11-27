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
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: verifier);

        var response = await GetPackageAsync(packageId);
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
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: Verifier(1));

        var response = await GetPackageAsync(packageId, unlockProof: Verifier(2));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, (await GetShellAsync(packageId)).CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithMalformedProof_Returns401()
    {
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: Verifier());

        var response = await GetPackageAsync(packageId, unlockProof: "not-base64url!");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, (await GetShellAsync(packageId)).CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithCorrectProof_ReturnsContentAndConsumesOneAccess()
    {
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: verifier);

        var details = await GetPackageDetailsAsync(packageId, unlockProof: verifier);

        Assert.HasCount(1, details.Items);
        Assert.AreEqual(1, details.Package.CurrentAccessCount);
        Assert.AreEqual(1, (await GetShellAsync(packageId)).CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_ReportsWhetherThePackageIsProtected()
    {
        var (openPackageId, _) = await CreatePackageWithItemAsync();

        var open = await GetPackageDetailsAsync(openPackageId);
        Assert.IsFalse(open.Package.IsProtected);

        var verifier = Verifier();
        var (protectedPackageId, _) = await CreatePackageWithItemAsync(
            unlockVerifier: verifier);

        var locked = await GetPackageDetailsAsync(
            protectedPackageId, unlockProof: verifier);
        Assert.IsTrue(locked.Package.IsProtected);
    }

    [TestMethod]
    public async Task GetPackage_WithSessionAndProof_ReusesTheAccess()
    {
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: verifier);

        var first = await GetPackageDetailsAsync(packageId, unlockProof: verifier);
        var second = await GetPackageDetailsAsync(
            packageId,
            unlockProof: verifier,
            accessSession: first.AccessSession.Token);

        Assert.AreEqual(first.AccessSession.Token, second.AccessSession.Token);
        Assert.AreEqual(1, second.Package.CurrentAccessCount);
    }

    [TestMethod]
    public async Task GetPackage_WithSessionButWithoutProof_Returns401AndKeepsTheAccessCount()
    {
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: verifier);

        var first = await GetPackageDetailsAsync(packageId, unlockProof: verifier);

        var response = await GetPackageAsync(
            packageId,
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
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: verifier);

        await GetPackageDetailsAsync(packageId, unlockProof: verifier);

        var second = await GetPackageAsync(packageId, unlockProof: verifier);
        Assert.AreEqual(HttpStatusCode.Gone, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Package access limit reached", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_WhenExhaustedPackageHasMissingBlob_Returns410WithoutReadingIt()
    {
        // An open performs zero blob reads, so a missing object must not turn
        // a second, rejected read of an exhausted package into a 500.
        var (packageId, contentId) = await CreatePackageWithItemAsync(
            bytes: SmallItemBytes, maxAccessCount: 1);

        await GetPackageDetailsAsync(packageId);

        var content = await Services.GetRequiredService<IContentStorageHandler>()
            .GetContentAsync(contentId);
        Assert.IsNotNull(content);
        await S3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = content.Url
        });

        var response = await GetPackageAsync(packageId);

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package access limit reached", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_WhenProtectedPackageIsExpired_Returns410WithoutProof()
    {
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: Verifier());

        TimeProvider.SetUtcNow(TimeProvider.GetUtcNow().AddHours(2));

        var response = await GetPackageAsync(packageId);

        // Expiry is not content, so 410 may be answered before the proof.
        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package expired", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_ForUnprotectedPackage_NeedsNoProof()
    {
        var (packageId, _) = await CreatePackageWithItemAsync();

        var response = await GetPackageAsync(packageId);

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
