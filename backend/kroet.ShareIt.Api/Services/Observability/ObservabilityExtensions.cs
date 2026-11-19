using System.Reflection;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Storage.Postgres;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace kroet.ShareIt.Api.Services.Observability;

/// <summary>
/// Registers application metrics, traces and logs when an OTLP endpoint is
/// configured. All three signals share one OpenTelemetry resource.
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
        var logsEndpoint = configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"];

        var metricsExportEnabled = !sdkDisabled &&
                                   (!string.IsNullOrWhiteSpace(sharedEndpoint) ||
                                    !string.IsNullOrWhiteSpace(metricsEndpoint));
        var tracesExportEnabled = !sdkDisabled &&
                                  (!string.IsNullOrWhiteSpace(sharedEndpoint) ||
                                   !string.IsNullOrWhiteSpace(tracesEndpoint));
        var logsExportEnabled = !sdkDisabled &&
                                (!string.IsNullOrWhiteSpace(sharedEndpoint) ||
                                 !string.IsNullOrWhiteSpace(logsEndpoint));

        // Snapshots are SQL-only. In-memory storage deliberately has no
        // snapshot provider, so only register the refresher when both the SQL
        // store and OTLP metrics export are configured.
        if (metricsExportEnabled &&
            configuration.GetSection(SqlStorageSettings.SectionName).Exists())
        {
            builder.Services.AddHostedService<MetricsSnapshotService>();
        }

        if (metricsExportEnabled || tracesExportEnabled || logsExportEnabled)
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

            if (logsExportEnabled)
            {
                openTelemetry.WithLogging(
                    logging => logging.AddOtlpExporter(),
                    options =>
                    {
                        options.IncludeScopes = true;
                        options.IncludeFormattedMessage = true;
                        options.ParseStateValues = true;
                    });
            }
        }

        return builder;
    }

    internal static string ResolveServiceVersion()
        => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(Program).Assembly.GetName().Version?.ToString()
           ?? "unknown";
}
