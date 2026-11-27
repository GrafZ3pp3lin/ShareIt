using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Models;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Storage;

/// <summary>
/// Generates opaque access-session tokens and delegates persistence to the
/// content store so the access claim and session insert commit atomically.
/// </summary>
public sealed class AccessSessionService : IAccessSessionService
{
    private readonly IContentStorageHandler _contentStorageHandler;
    private readonly DownloadSettings _settings;
    private readonly TimeProvider _timeProvider;

    public AccessSessionService(
        IContentStorageHandler contentStorageHandler,
        IOptions<DownloadSettings> downloadSettings,
        TimeProvider timeProvider)
    {
        _contentStorageHandler = contentStorageHandler;
        _settings = downloadSettings.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<AccessSessionCreationResult> CreateAsync(
        Guid packageId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var token = OpaqueToken.Create();
        var expiresAt = now.AddMinutes(_settings.AccessSessionLifetimeMinutes);

        var session = new AccessSession
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            UserId = userId,
            TokenHash = OpaqueToken.Hash(token),
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        var claim = await _contentStorageHandler.CreateAccessSessionAsync(session, cancellationToken);
        if (!claim.Success)
        {
            return new AccessSessionCreationResult(false, null, claim.NewAccessCount, claim.MaxAccessCount);
        }

        return new AccessSessionCreationResult(
            true,
            new AccessSessionTicket(token, expiresAt),
            claim.NewAccessCount,
            claim.MaxAccessCount);
    }

    /// <inheritdoc />
    public async Task<AccessSessionTicket?> ValidateAsync(
        string token,
        Guid packageId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (!OpaqueToken.IsWellFormed(token))
        {
            return null;
        }

        var session = await _contentStorageHandler.GetAccessSessionByTokenHashAsync(
            OpaqueToken.Hash(token),
            cancellationToken);

        if (session is null ||
            session.PackageId != packageId ||
            session.UserId != userId ||
            session.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return new AccessSessionTicket(token, session.ExpiresAt);
    }

    /// <inheritdoc />
    public Task<long> CleanupExpiredAsync(CancellationToken cancellationToken = default)
        => _contentStorageHandler.DeleteExpiredAccessSessionsAsync(_timeProvider.GetUtcNow(), cancellationToken);
}
