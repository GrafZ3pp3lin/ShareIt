using System.Security.Cryptography;
using Dapper;
using kroet.ShareIt.Api.Content.Configuration;
using kroet.ShareIt.Api.Content.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace kroet.ShareIt.Api.Content.Services;

/// <summary>
/// PostgreSQL implementation of the download token service using Dapper.
/// </summary>
public class SqlDownloadTokenService : IDownloadTokenService
{
    private readonly PostgreSqlDbSettings _settings;
    private readonly DownloadSettings _downloadSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqlDownloadTokenService> _logger;

    public SqlDownloadTokenService(IOptions<PostgreSqlDbSettings> settings, IOptions<DownloadSettings> downloadSettings, TimeProvider timeProvider,
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
    public async Task<TokenValidationResult> ValidateAndConsumeTokenAsync(string token, Guid clientId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = new NpgsqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Find and lock the token
            const string findSql = """
                                   SELECT 
                                       id, content_id, token, created_at, expires_at, 
                                       client_id, is_used, used_at
                                   FROM download_tokens
                                   WHERE token = @Token
                                   FOR UPDATE
                                   """;

            var tokenDoc = await connection.QuerySingleOrDefaultAsync<DownloadTokenDbDto>(findSql, new { Token = token }, transaction);

            if (tokenDoc == null)
            {
                _logger.LogWarning("Invalid download token attempted: {Token}", token[..Math.Min(8, token.Length)] + "...");
                await transaction.RollbackAsync(cancellationToken);
                return new TokenValidationResult(false, null, "Invalid token");
            }

            // Check if already used
            if (tokenDoc.IsUsed)
            {
                _logger.LogWarning("Attempted to reuse download token for content {ContentId}", tokenDoc.ContentId);
                await transaction.RollbackAsync(cancellationToken);
                return new TokenValidationResult(false, null, "Token has already been used");
            }

            // Check if expired
            if (tokenDoc.ExpiresAt <= now)
            {
                _logger.LogWarning("Expired download token attempted for content {ContentId}", tokenDoc.ContentId);
                await transaction.RollbackAsync(cancellationToken);
                return new TokenValidationResult(false, null, "Token has expired");
            }

            // Validate client ID
            if (tokenDoc.ClientId != clientId)
            {
                _logger.LogWarning("Client ID mismatch for token");
                await transaction.RollbackAsync(cancellationToken);
                return new TokenValidationResult(false, null, "Token is not valid for this client");
            }

            // Mark as used
            const string updateSql = """
                                     UPDATE download_tokens
                                     SET is_used = true, used_at = @UsedAt
                                     WHERE id = @Id
                                     """;

            await connection.ExecuteAsync(updateSql, new { tokenDoc.Id, UsedAt = now }, transaction);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogDebug("Successfully validated and consumed token for content {ContentId}", tokenDoc.ContentId);
            return new TokenValidationResult(true, tokenDoc.ContentId, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error validating and consuming token");
            throw;
        }
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
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    /// <summary>
    /// DTO for mapping database records.
    /// </summary>
    private class DownloadTokenDbDto
    {
        public Guid Id { get; init; }
        public Guid ContentId { get; init; }
        public string Token { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public Guid ClientId { get; init; }
        public bool IsUsed { get; init; }
        public DateTimeOffset? UsedAt { get; init; }
    }
}