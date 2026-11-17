namespace kroet.ShareIt.Api.Models;

/// <summary>
/// A short-lived grant created by a successful package open. The client sends
/// the token on package re-fetches and downloads; only its hash is stored.
/// </summary>
public sealed record AccessSession
{
    /// <summary>
    /// Unique identifier of the session row.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Package this session grants access to.
    /// </summary>
    public required Guid PackageId { get; init; }

    /// <summary>
    /// Recipient identity: an authenticated user id or a guest id.
    /// </summary>
    public required Guid ClientId { get; init; }

    /// <summary>
    /// Whether <see cref="ClientId"/> is a guest id rather than a user id.
    /// </summary>
    public required bool IsGuest { get; init; }

    /// <summary>
    /// SHA-256 hash of the session token, hex-encoded.
    /// </summary>
    public required string TokenHash { get; init; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the session stops authorizing package reads and downloads.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
