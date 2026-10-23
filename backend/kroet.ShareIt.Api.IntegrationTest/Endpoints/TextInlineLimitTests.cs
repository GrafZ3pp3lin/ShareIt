using System.Net;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint tests for the inline text limit in GET /package/{id}: text blobs
/// larger than <c>DownloadSettings:InlineTextLimitKb</c> must not be read into
/// memory and inlined into the response, but served via a download token like
/// files.
/// </summary>
[TestClass]
public sealed class TextInlineLimitTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        // A limit of zero disables inlining: every text item goes through the
        // download-token path.
        ["DownloadSettings:InlineTextLimitKb"] = "0"
    };

    private static readonly byte[] FileBytes = "hello world"u8.ToArray();

    [TestMethod]
    public async Task GetPackage_WithZeroInlineLimit_ServesTextViaDownloadToken()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);

        var textResponse = await SendAddTextAsync(packageId, "some shared text", guestId: guestId);
        var textBody = await textResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, textResponse.StatusCode, textBody);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        var text = details.Items.Single(item => item.Type == "text");
        Assert.IsNull(text.Text);
        Assert.IsFalse(string.IsNullOrEmpty(text.DownloadToken));
        Assert.IsFalse(string.IsNullOrEmpty(text.DownloadUrl));
        Assert.IsNotNull(text.TokenExpiresAt);

        // The text blob is reachable through the token like any file.
        var download = await SendDownloadAsync(text.DownloadUrl!, text.DownloadToken!, guestId);
        var downloadBody = await download.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode, downloadBody);
        Assert.AreEqual("some shared text", downloadBody);
    }

    [TestMethod]
    public async Task Download_TextToken_IsSingleUse()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: FileBytes);
        await SendAddTextAsync(packageId, "some shared text", guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var text = details.Items.Single(item => item.Type == "text");

        var first = await SendDownloadAsync(text.DownloadUrl!, text.DownloadToken!, guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var second = await SendDownloadAsync(text.DownloadUrl!, text.DownloadToken!, guestId);
        Assert.AreEqual(HttpStatusCode.BadRequest, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.AreEqual("Invalid download token", problem.Title);
    }
}
