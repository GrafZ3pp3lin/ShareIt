using System.Net;
using System.Net.Sockets;
using kroet.ShareIt.Api.Services.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.Test.Services.Health;

[TestClass]
public sealed class HealthCheckProbeTests
{
    [TestMethod]
    public async Task RunAsync_WhenEndpointAnswers2xx_ReturnsZero()
    {
        await using var server = await StartTestServerAsync(() => Results.Ok());
        var url = GetAddress(server);

        Assert.AreEqual(0, await HealthCheckProbe.RunAsync($"{url}/health/ready"));
    }

    [TestMethod]
    public async Task RunAsync_WhenEndpointAnswers503_ReturnsNonZero()
    {
        await using var server = await StartTestServerAsync(
            () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        var url = GetAddress(server);

        Assert.AreEqual(1, await HealthCheckProbe.RunAsync($"{url}/health/ready"));
    }

    [TestMethod]
    public async Task RunAsync_WhenNothingListens_ReturnsNonZero()
    {
        Assert.AreEqual(1, await HealthCheckProbe.RunAsync($"http://127.0.0.1:{FreePort()}/health/ready"));
    }

    private static async Task<WebApplication> StartTestServerAsync(Func<IResult> result)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/health/ready", result);
        await app.StartAsync();
        return app;
    }

    private static string GetAddress(WebApplication app)
        => app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
