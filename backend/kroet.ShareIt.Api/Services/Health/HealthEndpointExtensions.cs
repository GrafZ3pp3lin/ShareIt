using kroet.ShareIt.Api.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace kroet.ShareIt.Api.Services.Health;

/// <summary>
/// Liveness and readiness health endpoints for orchestrators and load
/// balancers. Liveness only proves the process can respond; readiness also
/// verifies the backing stores (content metadata store and blob store).
/// Both endpoints answer with a JSON report body.
/// </summary>
public static class HealthEndpointExtensions
{
    private const string LiveTag = "live";
    private const string ReadyTag = "ready";

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the health checks backing the liveness and readiness
        /// endpoints.
        /// </summary>
        public IServiceCollection AddShareItHealthChecks()
        {
            services.AddHealthChecks()
                // Liveness: the process itself is the only dependency.
                .AddCheck("self", () => HealthCheckResult.Healthy("Process is responding."), [LiveTag], TimeSpan.FromSeconds(5))
                // Readiness: the backing stores must be reachable before the
                // API is allowed to take traffic.
                .Add(new HealthCheckRegistration(
                    "content-store",
                    sp => new ContentStorageHealthCheck(sp.GetRequiredService<IContentStorageHandler>()),
                    HealthStatus.Unhealthy,
                    [ReadyTag],
                    TimeSpan.FromSeconds(5)))
                .Add(new HealthCheckRegistration(
                    "blob-store",
                    sp => new BlobStorageHealthCheck(sp.GetRequiredService<IBlobStorageHandler>()),
                    HealthStatus.Unhealthy,
                    [ReadyTag],
                    TimeSpan.FromSeconds(5)));

            return services;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>
        /// Maps <c>GET /health/live</c> and <c>GET /health/ready</c> outside
        /// the versioned API surface. Unhealthy readiness reports answer with
        /// HTTP 503; both endpoints return the report as JSON.
        /// </summary>
        public void MapHealthEndpoints()
        {
            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(LiveTag),
                ResponseWriter = WriteHealthResponseAsync
            });

            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(ReadyTag),
                ResponseWriter = WriteHealthResponseAsync
            });
        }
    }

    /// <summary>
    /// Writes the health report as a compact JSON object instead of the
    /// default plain-text "Healthy"/"Unhealthy" body.
    /// </summary>
    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            Status = report.Status.ToString(),
            Entries = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => (object)new
                {
                    Status = entry.Value.Status.ToString(),
                    Description = entry.Value.Description,
                    DurationMs = entry.Value.Duration.TotalMilliseconds
                })
        });
    }

    private sealed class ContentStorageHealthCheck(IContentStorageHandler storage) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await storage.CheckHealthAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("Content metadata store reachable.")
                    : HealthCheckResult.Unhealthy("Content metadata store unreachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Content metadata store unreachable.", ex);
            }
        }
    }

    private sealed class BlobStorageHealthCheck(IBlobStorageHandler blobs) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await blobs.CheckHealthAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("Blob store reachable.")
                    : HealthCheckResult.Unhealthy("Blob store unreachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Blob store unreachable.", ex);
            }
        }
    }
}
