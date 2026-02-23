namespace kroet.ShareIt.Api.Models;

/// <summary>
/// MongoDB document for storing temporary download tokens.
/// </summary>
public sealed class DownloadToken
{
    /// <summary>
    /// Identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The content ID this token grants access to.
    /// </summary>
    public required Guid ContentId { get; init; }

    /// <summary>
    /// The cryptographically secure token string.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Timestamp when the token was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when the token expires.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Client ID that requested the token.
    /// Used for additional security validation.
    /// </summary>
    public Guid ClientId { get; init; }
}