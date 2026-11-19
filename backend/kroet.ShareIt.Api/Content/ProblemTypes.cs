namespace kroet.ShareIt.Api.Content;

/// <summary>
/// Stable RFC 7807 problem type identifiers for client-side error handling.
/// </summary>
public static class ProblemTypes
{
    /// <summary>
    /// The package itself is gone: it was deleted, expired, or exhausted.
    /// Content-specific 404s intentionally do not carry this type.
    /// </summary>
    public const string PackageGone = "urn:shareit:problem:package-gone";
}
