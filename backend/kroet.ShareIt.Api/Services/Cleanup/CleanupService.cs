using System.Diagnostics;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Services.Cleanup;

/// <summary>
/// Background service that periodically cleans up expired tokens and content that has reached its download limit.
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

        _logger.LogInformation("Content cleanup service stopped");
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("cleanup.run");

        try
        {
            _logger.LogDebug("Running content cleanup...");

            using var scope = _scopeFactory.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<IDownloadTokenService>();
            var contentStorageHandler = scope.ServiceProvider.GetRequiredService<IContentStorageHandler>();

            // Cleanup expired tokens
            var tokensDeleted = await tokenService.CleanupExpiredTokensAsync(cancellationToken);
            activity?.SetTag("shareit.cleanup.tokens_deleted", tokensDeleted);

            // Cleanup packages that have reached their access limit
            var packagesToDelete = await contentStorageHandler.GetPackagesToDeleteAsync(cancellationToken);
            activity?.SetTag("shareit.cleanup.packages_pending", packagesToDelete.Count);
            var packagesDeleted = 0;
            var packageDeletionSucceeded = true;

            try
            {
                packagesDeleted = await contentStorageHandler.DeletePackagesAsync(packagesToDelete, cancellationToken);
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

            if (packageDeletionSucceeded)
            {
                _metrics.PackageDeleted("cleanup", packagesDeleted);
                _heartbeat.RecordSuccess(_timeProvider.GetUtcNow());
            }

            activity?.SetTag("shareit.cleanup.packages_deleted", packagesDeleted);
            activity?.SetTag("shareit.cleanup.succeeded", packageDeletionSucceeded);

            if (tokensDeleted > 0 || packagesDeleted > 0)
            {
                _logger.LogInformation(
                    "Cleanup completed: {TokensDeleted} tokens, {PackagesDeleted} packages deleted",
                    tokensDeleted, packagesDeleted);
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

