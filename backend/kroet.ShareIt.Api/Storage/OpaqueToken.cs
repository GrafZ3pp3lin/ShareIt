using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace kroet.ShareIt.Api.Storage;

/// <summary>
/// Creates and verifies the opaque bearer tokens used for uploads and access
/// sessions. Only the hex SHA-256 hash is persisted; comparisons are constant
/// time so a token cannot be probed by timing.
/// </summary>
public static class OpaqueToken
{
    private const int TokenLengthBytes = 32;

    /// <summary>
    /// Length of the text <see cref="Create"/> produces: 32 random bytes
    /// encode to 43 unpadded base64url characters.
    /// </summary>
    public const int WellFormedLength = 43;

    /// <summary>
    /// Creates a fresh 32-byte token encoded as base64url.
    /// </summary>
    public static string Create()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenLengthBytes));

    /// <summary>
    /// Checks that a presented token has the shape <see cref="Create"/>
    /// produces: exactly <see cref="WellFormedLength"/> base64url characters.
    /// Every stored hash comes from such a token, so anything else cannot
    /// match one and can be rejected before it costs a hash or a lookup.
    /// </summary>
    public static bool IsWellFormed([NotNullWhen(true)] string? token)
    {
        if (token is not { Length: WellFormedLength })
        {
            return false;
        }

        foreach (var character in token)
        {
            if (character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-' or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Hashes a token for storage or lookup (hex SHA-256).
    /// </summary>
    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Checks a presented token against a stored hash in constant time. A
    /// missing, blank or malformed token never matches.
    /// </summary>
    public static bool Matches(string tokenHash, string? token)
    {
        if (!IsWellFormed(token))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(tokenHash),
            Encoding.ASCII.GetBytes(Hash(token)));
    }
}
