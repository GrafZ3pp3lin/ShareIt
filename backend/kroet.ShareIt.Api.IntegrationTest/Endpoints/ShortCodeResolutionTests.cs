using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Locator resolution rejects malformed input before touching storage and
/// keeps the response uncacheable.
/// </summary>
[TestClass]
public sealed class ShortCodeResolutionTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task ResolveShortCode_WithInvalidFormat_ReturnsNotFound()
    {
        var response = await ResolveShortCodeAsync("AB!");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Short code not found", problem.Title);
    }
}
