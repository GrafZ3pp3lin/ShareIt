using System.Net;
using kroet.ShareIt.Api.Content;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// If every generated locator is taken, code-mode package creation fails with
/// 503 rather than writing an invalid or half-coded package.
/// </summary>
[TestClass]
public sealed class ShortCodeExhaustionTests : ShareItApiTestBase
{
    protected override Action<IServiceCollection>? ConfigureTestServices =>
        services => services.AddSingleton<IShortCodeGenerator, ConstantShortCodeGenerator>();

    [TestMethod]
    public async Task CreatePackage_WithCodeMode_WhenNoLocatorCanBeAllocated_Returns503()
    {
        var first = await CreateShortCodePackageAsync();
        Assert.AreEqual("AAA", first.ShortCode);

        var response = await SendCreatePackageAsync(
            PackageRequest(useShortCode: true, unlockVerifier: Verifier()),
            unlockProof: Verifier());

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Short code unavailable", problem.Title);
    }

    private sealed class ConstantShortCodeGenerator : IShortCodeGenerator
    {
        public string Next() => "AAA";
    }
}
