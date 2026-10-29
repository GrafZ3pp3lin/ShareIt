using System.Net;
using kroet.Permissions;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Reaches the handlers' defensive identity checks by bypassing the permission
/// layer that normally rejects these requests first.
/// </summary>
[TestClass]
public sealed class PermissionBypassEndpointTests : ShareItApiTestBase
{
    protected override Action<IServiceCollection> ConfigureTestServices => services =>
    {
        services.AddSingleton<IPermissionManager, AlwaysGrantPermissionManager>();
    };

    [TestMethod]
    public async Task GetPackagePreview_WithoutIdentity_Returns400()
    {
        var response = await Client.GetAsync($"/api/v1/content/package/{Guid.NewGuid()}/preview");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }

    [TestMethod]
    public async Task GetPackagePreview_AsGuest_Returns401()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/content/package/{Guid.NewGuid()}/preview");
        request.Headers.Add("X-Guest-ID", Guid.NewGuid().ToString());

        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Authentication required", problem.Title);
    }

    [TestMethod]
    public async Task DeletePackage_WithoutIdentity_Returns400()
    {
        var response = await Client.DeleteAsync($"/api/v1/content/packages/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("User ID required", problem.Title);
    }
}
