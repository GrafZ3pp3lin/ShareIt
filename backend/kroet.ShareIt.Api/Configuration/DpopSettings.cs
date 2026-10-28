namespace kroet.ShareIt.Api.Configuration;

/// <summary>
/// Configuration for DPoP (RFC 9449) sender-constrained token validation.
/// </summary>
public sealed class DpopSettings : ISettings
{
    /// <summary>
    /// When true (default), access tokens must be DPoP-bound
    /// (<c>cnf.jkt</c>) and every request must carry a valid proof. When false
    /// (rollout mode), only tokens that already carry a DPoP binding are
    /// proof-validated; plain bearer tokens pass.
    /// </summary>
    public bool Required { get; set; } = true;

    /// <summary>
    /// Maximum age of a DPoP proof (allowed skew around the proof's <c>iat</c>)
    /// and the window for which a proof's <c>jti</c> is remembered for replay
    /// detection. RFC 9449 recommends 300 seconds.
    /// Default: 300.
    /// </summary>
    public int ProofMaxAgeSeconds { get; set; } = 300;

    public static string SectionName => "Auth:Dpop";
}
