namespace kroet.ShareIt.Api.Storage;

/// <summary>
/// The client-visible part of an access session. The token is returned only
/// when the session is created or re-validated; storage keeps just its hash.
/// </summary>
public sealed record AccessSessionTicket(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Result of creating an access session. A failed claim means the package is
/// missing or its access limit has been reached; no counter was changed.
/// </summary>
public sealed record AccessSessionCreationResult(
    bool Success,
    AccessSessionTicket? Ticket,
    int NewAccessCount,
    int? MaxAccessCount
);

/// <summary>
/// Creates, validates, and cleans up recipient-bound package access sessions.
/// </summary>
public interface IAccessSessionService
{
    /// <summary>
    /// Atomically claims one access and creates a session for the caller who
    /// just opened the package successfully.
    /// </summary>
    Task<AccessSessionCreationResult> CreateAsync(
        Guid packageId,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a session token without consuming or extending it. Returns
    /// null when the token is unknown, expired, or bound to another recipient
    /// or package.
    /// </summary>
    Task<AccessSessionTicket?> ValidateAsync(
        string token,
        Guid packageId,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired sessions.
    /// </summary>
    Task<long> CleanupExpiredAsync(CancellationToken cancellationToken = default);
}
