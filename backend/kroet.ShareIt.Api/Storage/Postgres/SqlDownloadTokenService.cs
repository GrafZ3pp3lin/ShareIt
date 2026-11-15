using System.Buffers.Text;
using System.Security.Cryptography;
using Dapper;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace kroet.ShareIt.Api.Storage.Postgres;

/// <summary>
/// PostgreSQL implementation of the download token service using Dapper.
/// </summary>
public class SqlDownloadTokenService : IDownloadTokenService
{
    static SqlDownloadTokenService()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private readonly SqlStorageSettings _settings;
    private readonly DownloadSettings _downloadSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqlDownloadTokenService> _logger;

    public SqlDownloadTokenService(IOptions<SqlStorageSettings> settings, IOptions<DownloadSettings> downloadSettings, TimeProvider timeProvider,
        ILogger<SqlDownloadTokenService> logger)
    {
        _settings = settings.Value;
        _downloadSettings = downloadSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DownloadToken> GenerateTokenAsync(Guid contentId, Guid clientId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var token = GenerateSecureToken();

        var downloadToken = new DownloadToken
        {
            Id = Guid.NewGuid(),
            ContentId = contentId,
            Token = token,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_downloadSettings.DefaultTokenExpirationMinutes),
            ClientId = clientId,
        };

        const string insertSql = """
                                 INSERT INTO download_tokens (
                                     id, content_id, token, created_at, expires_at, client_id, is_used
                                 ) VALUES (
                                     @Id, @ContentId, @Token, @CreatedAt, @ExpiresAt, @ClientId, @IsUsed
                                 )
                                 """;

        await using var connection = new NpgsqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(insertSql, new
        {
            downloadToken.Id,
            downloadToken.ContentId,
            downloadToken.Token,
            downloadToken.CreatedAt,
            downloadToken.ExpiresAt,
            downloadToken.ClientId,
            IsUsed = false
        });

        _logger.LogDebug("Generated download token for content {ContentId}, expires at {ExpiresAt}", contentId, downloadToken.ExpiresAt);

        return downloadToken;
    }

    /// <inheritdoc />
    public async Task<TokenValidationResult> ValidateTokenAsync(string token, Guid clientId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        const string findSql = """
                               SELECT
                                   id, content_id, expires_at, client_id, is_used
                               FROM download_tokens
                               WHERE token = @Token
                               """;

        await using var connection = new NpgsqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var tokenDoc = await connection.QuerySingleOrDefaultAsync<DownloadTokenDbDto>(findSql, new { Token = token });

        if (tokenDoc is null)
        {
            _logger.LogWarning("Invalid download token attempted: {TokenPrefix}...", token[..Math.Min(8, token.Length)]);
            return new TokenValidationResult(false, null, "Invalid token");
        }

        if (tokenDoc.IsUsed)
        {
            _logger.LogWarning("Attempted to reuse download token for content {ContentId}", tokenDoc.ContentId);
            return new TokenValidationResult(false, null, "Token has already been used");
        }

        if (tokenDoc.ExpiresAt <= now)
        {
            _logger.LogWarning("Expired download token attempted for content {ContentId}", tokenDoc.ContentId);
            return new TokenValidationResult(false, null, "Token has expired");
        }

        if (tokenDoc.ClientId != clientId)
        {
            _logger.LogWarning("Client ID mismatch for token");
            return new TokenValidationResult(false, null, "Token is not valid for this client");
        }

        return new TokenValidationResult(true, tokenDoc.ContentId, null);
    }

    /// <inheritdoc />
    public async Task<TokenValidationResult> ConsumeTokenAsync(string token, Guid clientId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        // One guarded statement: concurrent requests carrying the same token
        // can only win once. The expiry/client checks are repeated here so a
        // token cannot be consumed after validation has gone stale.
        const string consumeSql = """
                                  UPDATE download_tokens
                                  SET is_used = true, used_at = @UsedAt
                                  WHERE token = @Token
                                    AND is_used = false
                                    AND expires_at > @UsedAt
                                    AND client_id = @ClientId
                                  RETURNING content_id
                                  """;

        await using var connection = new NpgsqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var contentId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            consumeSql,
            new { Token = token, ClientId = clientId, UsedAt = now });

        if (contentId is null)
        {
            // Unlike validation, we no longer know which reason applied without
            // a second read; this only happens on a lost race or a stale token.
            _logger.LogWarning("Download token could not be consumed (already used, expired or wrong client)");
            return new TokenValidationResult(false, null, "Token has already been used");
        }

        _logger.LogDebug("Consumed download token for content {ContentId}", contentId.Value);
        return new TokenValidationResult(true, contentId.Value, null);
    }

    /// <inheritdoc />
    public async Task<long> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var usedTokenCutoff = now.AddHours(-_downloadSettings.UsedTokenRetentionHours);

        const string deleteSql = """
                                 DELETE FROM download_tokens
                                 WHERE expires_at < @Now
                                    OR (is_used = true AND used_at < @UsedTokenCutoff)
                                 """;

        await using var connection = new NpgsqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var deletedCount = await connection.ExecuteAsync(deleteSql, new { Now = now, UsedTokenCutoff = usedTokenCutoff });

        if (deletedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired/used download tokens", deletedCount);
        }

        return deletedCount;
    }

    /// <summary>
    /// Generates a cryptographically secure token string.
    /// </summary>
    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// DTO for mapping database records.
    /// </summary>
    private class DownloadTokenDbDto
    {
        public Guid Id { get; init; }
        public Guid ContentId { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public Guid ClientId { get; init; }
        public bool IsUsed { get; init; }
    }
}
