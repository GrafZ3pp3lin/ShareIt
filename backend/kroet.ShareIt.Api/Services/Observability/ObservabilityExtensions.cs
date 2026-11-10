using System.Reflection;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Storage.Postgres;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace kroet.ShareIt.Api.Services.Observability;

/// <summary>
/// Registers application metrics and traces when an OTLP endpoint is
/// configured. Serilog's OTLP sink is configured separately in appsettings,
/// keeping logging configuration out of code.
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddMetrics();
        builder.Services.AddSingleton<ShareItMetrics>();
        builder.Services.AddSingleton<CleanupHeartbeat>();

        var configuration = builder.Configuration;
        var sdkDisabled = configuration.GetValue("OTEL_SDK_DISABLED", false);
        var sharedEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var metricsEndpoint = configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"];
        var tracesEndpoint = configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"];

        var metricsExportEnabled = !sdkDisabled &&
                                   (!string.IsNullOrWhiteSpace(sharedEndpoint) ||
                                    !string.IsNullOrWhiteSpace(metricsEndpoint));
        var tracesExportEnabled = !sdkDisabled &&
                                  (!string.IsNullOrWhiteSpace(sharedEndpoint) ||
                                   !string.IsNullOrWhiteSpace(tracesEndpoint));

        // Snapshots are SQL-only. In-memory storage deliberately has no
        // snapshot provider, so only register the refresher when both the SQL
        // store and OTLP metrics export are configured.
        if (metricsExportEnabled &&
            configuration.GetSection(SqlStorageSettings.SectionName).Exists())
        {
            builder.Services.AddHostedService<MetricsSnapshotService>();
        }

        if (metricsExportEnabled || tracesExportEnabled)
        {
            var openTelemetry = builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService("shareit-api", serviceVersion: ResolveServiceVersion())
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment.name"] = builder.Environment.EnvironmentName
                    }));

            if (metricsExportEnabled)
            {
                openTelemetry.WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(ShareItMetrics.MeterName)
                    .AddOtlpExporter());
            }

            if (tracesExportEnabled)
            {
                openTelemetry.WithTracing(tracing => tracing
                    .AddSource(ShareItActivitySource.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddAWSInstrumentation()
                    .AddOtlpExporter());
            }
        }

        builder.Services.AddSerilog((_, loggerConfiguration) =>
            loggerConfiguration.ReadFrom.Configuration(builder.Configuration));

        return builder;
    }

    internal static string ResolveServiceVersion()
        => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(Program).Assembly.GetName().Version?.ToString()
           ?? "unknown";
}
