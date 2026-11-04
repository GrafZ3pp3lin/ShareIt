using System.Security.Cryptography;

namespace kroet.ShareIt.Api.Content;

/// <summary>
/// Validation helpers for the package unlock verifier and the proof header.
/// The value is compared in constant time and must never be logged.
/// </summary>
internal static class UnlockProof
{
    /// <summary>
    /// HMAC-SHA256 produces a 32-byte verifier.
    /// </summary>
    public const int VerifierLength = 32;

    /// <summary>
    /// Upper bound for the opaque per-item metadata blob (name + MIME type).
    /// </summary>
    public const int MaxMetaBytes = 4 * 1024;

    /// <summary>
    /// True when <paramref name="verifier"/> is a base64url string encoding
    /// exactly 32 bytes.
    /// </summary>
    public static bool TryValidateVerifier(string? verifier, out byte[] bytes)
    {
        bytes = [];
        return Base64UrlText.TryDecode(verifier, out bytes) && bytes.Length == VerifierLength;
    }

    /// <summary>
    /// Constant-time comparison of the stored verifier with the request proof.
    /// Missing/malformed proofs are rejected without throwing.
    /// </summary>
    public static bool Matches(string? storedVerifier, string? providedProof)
    {
        if (!TryValidateVerifier(storedVerifier, out var stored))
        {
            return false;
        }

        return Base64UrlText.TryDecode(providedProof, out var provided)
               && provided.Length == stored.Length
               && CryptographicOperations.FixedTimeEquals(stored, provided);
    }
}
