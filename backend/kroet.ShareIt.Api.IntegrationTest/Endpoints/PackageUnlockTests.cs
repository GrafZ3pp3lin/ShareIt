using System.Net;
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
            guestId: guestId, maxDownloadCount: 1, unlockVerifier: verifier);

        var response = await GetPackageAsync(packageId, guestId: guestId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, body);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Unlock required", problem.Title);

        // No metadata, no items, no names, no proof echo.
        Assert.IsFalse(body.Contains("Items", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains("Payload", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains("DownloadToken", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains(verifier, StringComparison.Ordinal), body);

        var shell = await GetShellAsync(packageId);
        Assert.AreEqual(0, shell.CurrentDownloadCount, "A rejected unlock must not consume an access.");
    }

    [TestMethod]
    public async Task GetPackage_WithWrongProof_Returns401AndKeepsTheAccessCount()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxDownloadCount: 1, unlockVerifier: Verifier(1));

        var response = await GetPackageAsync(packageId, guestId: guestId, unlockProof: Verifier(2));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, (await GetShellAsync(packageId)).CurrentDownloadCount);
    }

    [TestMethod]
    public async Task GetPackage_WithMalformedProof_Returns401()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxDownloadCount: 1, unlockVerifier: Verifier());

        var response = await GetPackageAsync(packageId, guestId: guestId, unlockProof: "not-base64url!");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, (await GetShellAsync(packageId)).CurrentDownloadCount);
    }

    [TestMethod]
    public async Task GetPackage_WithCorrectProof_ReturnsContentAndConsumesOneAccess()
    {
        var guestId = Guid.NewGuid();
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxDownloadCount: 1, unlockVerifier: verifier);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId, unlockProof: verifier);

        Assert.HasCount(1, details.Items);
        Assert.AreEqual(1, details.Package.CurrentDownloadCount);
        Assert.AreEqual(1, (await GetShellAsync(packageId)).CurrentDownloadCount);
    }

    [TestMethod]
    public async Task GetPackage_WithCorrectProofAtAccessLimit_Returns410OnSecondRead()
    {
        var guestId = Guid.NewGuid();
        var verifier = Verifier();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxDownloadCount: 1, unlockVerifier: verifier);

        await GetPackageDetailsAsync(packageId, guestId: guestId, unlockProof: verifier);

        var second = await GetPackageAsync(packageId, guestId: guestId, unlockProof: verifier);
        Assert.AreEqual(HttpStatusCode.Gone, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Package access limit reached", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_WhenProtectedPackageIsExpired_Returns410WithoutProof()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            guestId: guestId, maxDownloadCount: 1, unlockVerifier: Verifier());

        TimeProvider.UtcNow = TimeProvider.UtcNow.AddHours(2);

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
