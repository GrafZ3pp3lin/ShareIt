using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
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
    private const int TokenLengthBytes = 32;

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
        Guid clientId,
        bool isGuest,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenLengthBytes));
        var expiresAt = now.AddMinutes(_settings.AccessSessionLifetimeMinutes);

        var session = new AccessSession
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            ClientId = clientId,
            IsGuest = isGuest,
            TokenHash = HashToken(token),
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
        Guid clientId,
        bool isGuest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var session = await _contentStorageHandler.GetAccessSessionByTokenHashAsync(
            HashToken(token),
            cancellationToken);

        if (session is null ||
            session.PackageId != packageId ||
            session.ClientId != clientId ||
            session.IsGuest != isGuest ||
            session.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return new AccessSessionTicket(token, session.ExpiresAt);
    }

    /// <inheritdoc />
    public Task<long> CleanupExpiredAsync(CancellationToken cancellationToken = default)
        => _contentStorageHandler.DeleteExpiredAccessSessionsAsync(_timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>
    /// Hashes the token before lookup so a database read never exposes a
    /// usable session token.
    /// </summary>
    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
