using System.Net;
using kroet.ShareIt.Api.Content;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Code access is opt-in at package creation. The server allocates only the
/// three-character locator and retries collisions; the browser keeps the other
/// three characters of the user-facing code.
/// </summary>
[TestClass]
public sealed class ShortCodeCreationTests : ShareItApiTestBase
{
    private readonly SequenceShortCodeGenerator _generator = new("AAA", "AAA", "BBB");

    protected override Action<IServiceCollection>? ConfigureTestServices =>
        services => services.AddSingleton<IShortCodeGenerator>(_generator);

    [TestMethod]
    public async Task CreatePackage_WithoutCodeMode_HasNoLocator()
    {
        var created = await CreatePackageOwnerAsync(guestId: Guid.NewGuid());

        Assert.IsNull(created.ShortCode);
        Assert.IsNull(_generator.LastIssued);
    }

    [TestMethod]
    public async Task CreatePackage_WithCodeMode_RetriesAConflictingLocator()
    {
        var guestId = Guid.NewGuid();

        var first = await CreateShortCodePackageAsync(guestId: guestId);
        Assert.AreEqual("AAA", first.ShortCode);

        var second = await CreateShortCodePackageAsync(guestId: guestId);
        Assert.AreEqual("BBB", second.ShortCode);

        var resolved = await ResolveShortCodeAsync("BBB");
        Assert.AreEqual(HttpStatusCode.OK, resolved.StatusCode);
        Assert.AreEqual(second.Id, (await ReadJsonAsync<ResolveShortCodeResponse>(resolved)).PackageId);
    }

    [TestMethod]
    public async Task CreatePackage_WithCodeMode_IsIdempotentForTheOwner()
    {
        var guestId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        var verifier = Verifier();
        var first = await CreateShortCodePackageAsync(
            packageId: packageId, guestId: guestId, verifier: verifier);
        var retry = await SendCreatePackageAsync(
            packageId: packageId, guestId: guestId, unlockVerifier: verifier, useShortCode: true);

        var body = await retry.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, body);
        var second = await ReadJsonAsync<ContentPackageOwnerResponse>(retry);

        Assert.AreEqual(first.ShortCode, second.ShortCode);
        Assert.AreEqual(1, _generator.Issued);
    }

    [TestMethod]
    public async Task CreatePackage_WithCodeMode_RetryWithADifferentVerifier_ReturnsConflict()
    {
        var guestId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        await CreateShortCodePackageAsync(packageId: packageId, guestId: guestId, verifier: Verifier(1));

        var response = await SendCreatePackageAsync(
            packageId: packageId,
            guestId: guestId,
            unlockVerifier: Verifier(2),
            useShortCode: true);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package id already in use", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithCodeMode_RetryAgainstARegularPackage_ReturnsConflict()
    {
        var guestId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        await CreatePackageOwnerAsync(packageId: packageId, guestId: guestId);

        var response = await SendCreatePackageAsync(
            packageId: packageId,
            guestId: guestId,
            unlockVerifier: Verifier(),
            useShortCode: true);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package id already in use", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithCodeMode_ClampsLifetimeAndAccessCount()
    {
        var userId = Guid.NewGuid();

        var response = await SendCreatePackageAsync(
            new CreatePackageRequest
            {
                Id = Guid.NewGuid(),
                UseShortCode = true,
                UnlockVerifier = Verifier(),
                TimeToLiveHours = 720,
                MaxAccessCount = 10
            },
            userId: userId,
            unlockProof: Verifier());

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);

        var created = await ReadJsonAsync<ContentPackageOwnerResponse>(response);
        Assert.AreEqual(1, created.MaxAccessCount);
        Assert.IsTrue(created.IsProtected);
        Assert.IsLessThanOrEqualTo(
            TimeSpan.FromMinutes(10),
            created.ExpiresAt - TimeProvider.GetUtcNow());
        Assert.IsGreaterThan(TimeSpan.FromMinutes(9), created.ExpiresAt - TimeProvider.GetUtcNow());
    }

    [TestMethod]
    public async Task CreatePackage_WithCodeMode_RequiresAVerifier()
    {
        var response = await SendCreatePackageAsync(
            new CreatePackageRequest
            {
                Id = Guid.NewGuid(),
                UseShortCode = true,
                TimeToLiveHours = 1,
                MaxAccessCount = 1
            },
            guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Unlock verifier required", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_WithCodeMode_RequiresAMatchingUnlockProof()
    {
        var verifier = Verifier(1);
        var response = await SendCreatePackageAsync(
            new CreatePackageRequest
            {
                Id = Guid.NewGuid(),
                UseShortCode = true,
                UnlockVerifier = verifier,
                TimeToLiveHours = 1,
                MaxAccessCount = 1
            },
            guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Unlock proof required", problem.Title);
    }

    private sealed class SequenceShortCodeGenerator(params string[] codes) : IShortCodeGenerator
    {
        private readonly Queue<string> _codes = new(codes);

        public int Issued { get; private set; }

        public string? LastIssued { get; private set; }

        public string Next()
        {
            Issued++;
            LastIssued = _codes.TryDequeue(out var code)
                ? code
                : throw new InvalidOperationException("The test short-code sequence is exhausted.");
            return LastIssued;
        }
    }
}
