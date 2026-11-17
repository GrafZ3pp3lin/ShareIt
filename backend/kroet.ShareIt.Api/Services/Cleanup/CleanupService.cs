using System.Diagnostics;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Services.Cleanup;

/// <summary>
/// Background service that periodically cleans up expired access sessions,
/// expired packages, exhausted packages after their retention window, and
/// queued blob deletions.
/// </summary>
public class CleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CleanupSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly CleanupHeartbeat _heartbeat;
    private readonly ShareItMetrics _metrics;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<CleanupSettings> settings,
        TimeProvider timeProvider,
        CleanupHeartbeat heartbeat,
        ShareItMetrics metrics,
        ILogger<CleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _heartbeat = heartbeat;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Content cleanup service started. Running every {Interval} minutes",
            _settings.CleanupIntervalMinutes);
        
        using var periodicTimer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.CleanupIntervalMinutes));
        
        try
        {
            while (await periodicTimer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunCleanupAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during content cleanup");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("cleanup.run");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var accessSessionService = scope.ServiceProvider.GetRequiredService<IAccessSessionService>();
            var contentStorageHandler = scope.ServiceProvider.GetRequiredService<IContentStorageHandler>();
            var storageHandler = scope.ServiceProvider.GetRequiredService<IStorageHandler>();

            // Cleanup expired access sessions. This is independent of package
            // metadata deletion: a transient session-cleanup failure must not
            // skip the rest of the cleanup pass.
            var sessionsDeleted = 0L;
            var sessionCleanupSucceeded = true;
            try
            {
                sessionsDeleted = await accessSessionService.CleanupExpiredAsync(cancellationToken);
                activity?.SetTag("shareit.cleanup.sessions_deleted", sessionsDeleted);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                sessionCleanupSucceeded = false;
                activity?.SetTag("shareit.cleanup.session_cleanup_succeeded", false);
                activity?.AddException(ex);
                _logger.LogError(ex, "Error cleaning up expired access sessions during cleanup");
            }

            // Cleanup expired packages and exhausted packages whose download
            // retention period has elapsed. Metadata deletion and blob queueing
            // commit together; draining that queue is a separate step below.
            var packagesToDelete = await contentStorageHandler.GetPackagesToDeleteAsync(cancellationToken);
            activity?.SetTag("shareit.cleanup.packages_pending", packagesToDelete.Count);
            var packagesDeleted = 0;
            var packageDeletionSucceeded = true;

            try
            {
                packagesDeleted = await storageHandler.DeletePackagesAsync(packagesToDelete, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                packageDeletionSucceeded = false;
                activity?.SetStatus(ActivityStatusCode.Error, "package deletion failed");
                activity?.AddException(ex);
                _logger.LogError(ex, "Error deleting content packages during cleanup");
            }

            activity?.SetTag("shareit.cleanup.packages_deleted", packagesDeleted);
            activity?.SetTag("shareit.cleanup.succeeded", packageDeletionSucceeded);

            if (packageDeletionSucceeded)
            {
                _metrics.PackageDeleted("cleanup", packagesDeleted);
                if (sessionCleanupSucceeded)
                {
                    _heartbeat.RecordSuccess(_timeProvider.GetUtcNow());
                }
            }

            // Blob deletion is independent of package metadata cleanup. A queue
            // failure must not turn an already committed package deletion into
            // a failed run or lose its metric and heartbeat.
            var blobsDeleted = 0;
            try
            {
                blobsDeleted = await storageHandler.ProcessPendingBlobDeletionsAsync(cancellationToken);
                activity?.SetTag("shareit.cleanup.blobs_deleted", blobsDeleted);
                activity?.SetTag("shareit.cleanup.blob_drain_succeeded", true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetTag("shareit.cleanup.blob_drain_succeeded", false);
                _logger.LogError(ex, "Error processing queued blob deletions during cleanup");
            }

            if (sessionsDeleted > 0 || packagesDeleted > 0 || blobsDeleted > 0)
            {
                _logger.LogInformation(
                    "Cleanup completed: {SessionsDeleted} sessions, {PackagesDeleted} packages, {BlobsDeleted} blobs deleted",
                    sessionsDeleted, packagesDeleted, blobsDeleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cleanup failed");
            activity?.SetTag("shareit.cleanup.succeeded", false);
            activity?.AddException(ex);
            _logger.LogError(ex, "Error during content cleanup");
        }
    }
}

