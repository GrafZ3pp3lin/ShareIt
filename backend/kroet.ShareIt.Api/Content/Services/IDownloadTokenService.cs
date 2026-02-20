using kroet.ShareIt.Api.Content.Models;

namespace kroet.ShareIt.Api.Content.Services;

/// <summary>
/// Result of a token validation operation.
/// </summary>
public record TokenValidationResult(
    bool IsValid,
    Guid? ContentId,
    string? ErrorMessage
);

/// <summary>
/// Service for generating and validating temporary download tokens.
/// </summary>
public interface IDownloadTokenService
{
    /// <summary>
    /// Generates a new download token for the specified content.
    /// </summary>
    /// <param name="contentId">The content ID to generate a token for.</param>
    /// <param name="clientId">Client id for validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated download token document.</returns>
    Task<DownloadToken> GenerateTokenAsync(
        Guid contentId,
        Guid clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a download token and marks it as used if valid.
    /// </summary>
    /// <param name="token">The token string to validate.</param>
    /// <param name="clientId">The client ID making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with content ID if valid.</returns>
    Task<TokenValidationResult> ValidateAndConsumeTokenAsync(
        string token,
        Guid clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired and old used tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of tokens removed.</returns>
    Task<long> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);
}

