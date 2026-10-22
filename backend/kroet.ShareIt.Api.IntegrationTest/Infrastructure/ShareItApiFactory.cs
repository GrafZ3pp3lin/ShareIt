using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Boots the real API entry point (Program) on the in-memory test server with:
/// - in-memory storage for content, blobs and download tokens (no S3/Postgres required),
/// - a background cleanup interval long enough to never fire during a test run,
/// - the test authentication scheme driven by the X-Test-User header,
/// - optional per-test-configuration overrides.
/// </summary>
/// <remarks>
/// Configuration overrides are injected through <see cref="CreateHost"/> /
/// <see cref="IHostBuilder.ConfigureHostConfiguration"/>: with minimal-hosting
/// apps, WebApplicationFactory's <c>ConfigureAppConfiguration</c> runs too late
/// to influence <c>WebApplicationBuilder.Configuration</c> that Program.cs
/// consults while wiring storage (dotnet/aspnetcore#33876).
/// </remarks>
public sealed class ShareItApiFactory(IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _settings = settings ?? new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var overrides = new Dictionary<string, string?>
        {
            // Select the in-memory storage implementations for every storage concern.
            ["ContentStorage:InMemory:Enabled"] = "true",
            ["BlobStorage:InMemory:Enabled"] = "true",

            // The cleanup service would wipe all packages on its first tick
            // (in-memory storage reports every package as deletable).
            ["CleanupSettings:CleanupIntervalMinutes"] = "10080",
        };

        foreach (var (key, value) in _settings)
        {
            overrides[key] = value;
        }

        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(overrides));

        return base.CreateHost(builder);
    }
}
