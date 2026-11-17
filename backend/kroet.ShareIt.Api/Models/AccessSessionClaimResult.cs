namespace kroet.ShareIt.Api.Models;

/// <summary>
/// Result of atomically claiming one package access and creating its session.
/// On failure, <see cref="NewAccessCount"/> and <see cref="MaxAccessCount"/> are
/// zero/null; callers must only use them when <see cref="Success"/> is true.
/// </summary>
public sealed record AccessSessionClaimResult(
    bool Success,
    int NewAccessCount,
    int? MaxAccessCount
);
