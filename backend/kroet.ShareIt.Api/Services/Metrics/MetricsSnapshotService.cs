using System.Diagnostics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Storage;

namespace kroet.ShareIt.Api.Services.Metrics;

/// <summary>
/// Periodically refreshes the database-derived gauges and dependency health
/// metrics. Keeping the refresh here means metric collection never triggers
/// database or S3 calls.
/// </summary>
public sealed class MetricsSnapshotService(
    IMetricsSnapshotProvider metricsSnapshotProvider,
    IBlobStorageHandler blobStorageHandler,
    CleanupHeartbeat cleanupHeartbeat,
    ShareItMetrics metrics,
    TimeProvider timeProvider,
    ILogger<MetricsSnapshotService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private bool? _lastPostgresUp;
    private bool? _lastS3Up;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RefreshAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("metrics.snapshot");

        var postgresUp = true;
        try
        {
            var snapshot = await metricsSnapshotProvider.GetMetricsSnapshotAsync(
                timeProvider.GetUtcNow(),
                cancellationToken);
            metrics.SetSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            postgresUp = false;
            if (_lastPostgresUp != false)
            {
                logger.LogWarning(ex, "PostgreSQL metrics snapshot failed");
            }
        }

        var s3Up = true;
        try
        {
            s3Up = await blobStorageHandler.CheckHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            s3Up = false;
            if (_lastS3Up != false)
            {
                logger.LogWarning(ex, "S3 health check failed");
            }
        }

        if (_lastPostgresUp != postgresUp)
        {
            logger.LogInformation("PostgreSQL metrics dependency is {Status}", postgresUp ? "up" : "down");
            _lastPostgresUp = postgresUp;
        }

        if (_lastS3Up != s3Up)
        {
            logger.LogInformation("S3 metrics dependency is {Status}", s3Up ? "up" : "down");
            _lastS3Up = s3Up;
        }

        metrics.SetDependencyHealth(postgresUp, s3Up);
        metrics.SetCleanupLastSuccess(cleanupHeartbeat.LastSuccessUnixSeconds);

        activity?.SetTag("shareit.metrics.postgres_up", postgresUp);
        activity?.SetTag("shareit.metrics.s3_up", s3Up);
        if (!postgresUp || !s3Up)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "dependency unavailable");
        }
    }
}
