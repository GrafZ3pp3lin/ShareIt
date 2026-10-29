using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Boots the real API entry point (Program) on the test server with:
/// - either in-memory storage or caller-provided real storage configuration,
/// - a background cleanup interval long enough to never fire during a test run,
/// - the test authentication scheme driven by the X-Test-User header,
/// - optional per-test configuration and service overrides.
/// </summary>
/// <remarks>
/// Configuration overrides are injected through <see cref="CreateHost"/> /
/// <see cref="IHostBuilder.ConfigureHostConfiguration"/>: with minimal-hosting
/// apps, WebApplicationFactory's <c>ConfigureAppConfiguration</c> runs too late
/// to influence <c>WebApplicationBuilder.Configuration</c> that Program.cs
/// consults while wiring storage (dotnet/aspnetcore#33876).
/// </remarks>
public sealed class ShareItApiFactory(
    IReadOnlyDictionary<string, string?>? settings = null,
    bool useInMemoryStorage = true,
    Action<IServiceCollection>? configureTestServices = null)
    : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _settings = settings ?? new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Keep tests independent of local appsettings.{Environment}.json files.
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            configureTestServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var overrides = new Dictionary<string, string?>
        {
            // The cleanup service would wipe all packages on its first tick.
            ["CleanupSettings:CleanupIntervalMinutes"] = "10080",
        };

        if (useInMemoryStorage)
        {
            // Select the in-memory storage implementations for every storage concern.
            overrides["ContentStorage:InMemory:Enabled"] = "true";
            overrides["BlobStorage:InMemory:Enabled"] = "true";
        }

        foreach (var (key, value) in _settings)
        {
            overrides[key] = value;
        }

        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(overrides));

        return base.CreateHost(builder);
    }
}
