using System.Net;
using System.Net.Http.Json;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// The per-IP lookup limit uses the forwarded client address. This pins the
/// proxy trust model: the API must not be exposed directly, because a caller
/// can otherwise choose its own partition by spoofing X-Forwarded-For.
/// </summary>
[TestClass]
public sealed class ShortCodeLookupForwardedIpTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimitSettings:ShortCodeLookupPermitLimit"] = "1",
        ["RateLimitSettings:ShortCodeLookupWindowMinutes"] = "15"
    };

    [TestMethod]
    public async Task ResolveShortCode_UsesForwardedClientIpForThePerIpLimit()
    {
        var first = await ResolveFromAsync("198.51.100.10");
        Assert.AreEqual(HttpStatusCode.NotFound, first.StatusCode);

        var sameClient = await ResolveFromAsync("198.51.100.10");
        Assert.AreEqual(HttpStatusCode.TooManyRequests, sameClient.StatusCode);

        var otherClient = await ResolveFromAsync("198.51.100.11");
        Assert.AreEqual(HttpStatusCode.NotFound, otherClient.StatusCode);
    }

    private async Task<HttpResponseMessage> ResolveFromAsync(string forwardedFor)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/content/package/code/resolve")
        {
            Content = JsonContent.Create(new ResolveShortCodeRequest { Code = "AB!" }, options: JsonOptions)
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await Client.SendAsync(request);
    }
}
