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
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<CleanupSettings> settings,
        ILogger<CleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
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
        _logger.LogDebug("Running content cleanup...");

        using var scope = _scopeFactory.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IDownloadTokenService>();
        var contentStorageHandler = scope.ServiceProvider.GetRequiredService<IContentStorageHandler>();

        // Cleanup expired tokens
        var tokensDeleted = await tokenService.CleanupExpiredTokensAsync(cancellationToken);

        // Cleanup packages that have reached their access limit
        var packagesToDelete = await contentStorageHandler.GetPackagesToDeleteAsync(cancellationToken);
        var packagesDeleted = 0;
        
        try
        {
            packagesDeleted = await contentStorageHandler.DeletePackagesAsync(packagesToDelete, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting content packages during cleanup");
        }

        if (tokensDeleted > 0 || packagesDeleted > 0)
        {
            _logger.LogInformation(
                "Cleanup completed: {TokensDeleted} tokens, {PackagesDeleted} packages deleted",
                tokensDeleted, packagesDeleted);
        }
    }
}

