namespace kroet.ShareIt.Api.Content;

/// <summary>
/// The server half of the split access code. A user-facing code is six
/// characters (<c>ABC-234</c>): the first three are this server-generated
/// locator (<c>ABC -&gt; packageId</c>), the last three are a client secret that
/// is never sent to or stored by the server. Codes are case-insensitive, use
/// an unambiguous 31-character alphabet and never enter the key schedule.
/// </summary>
public static class ShortCode
{
    /// <summary>
    /// Fixed length of the server-side locator. Only the locator is stored and
    /// resolved by the API; it is a capability that must be rate-limited, but
    /// it is not the decryption secret.
    /// </summary>
    public const int Length = 3;

    /// <summary>
    /// Alphabet without 0/O, 1/I and L.
    /// </summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>
    /// How often package creation may retry a locator that is already in use.
    /// </summary>
    public const int MaxGenerationAttempts = 10;

    /// <summary>
    /// Number of failed unlock proofs that burns a short-code package. Five
    /// guesses out of 31^3 possible secrets is roughly 0.016%.
    /// </summary>
    public const int MaxFailedUnlockAttempts = 5;

    /// <summary>
    /// Short-code packages expire after ten minutes regardless of the identity
    /// or the requested lifetime: the code is meant for immediate transfer.
    /// </summary>
    public static TimeSpan PackageLifetime => TimeSpan.FromMinutes(10);

    private static readonly char[] Allowed = [.. Alphabet];

    /// <summary>
    /// Normalizes a locator from user input. Returns null when the value is not
    /// exactly one valid server-side locator.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (character is '-' || char.IsWhiteSpace(character))
            {
                continue;
            }

            // Reject non-ASCII before case mapping: Unicode folding can turn
            // a homoglyph such as U+017F into a valid ASCII letter.
            if (character > 0x7F)
            {
                return null;
            }

            var upper = char.ToUpperInvariant(character);
            if (Array.IndexOf(Allowed, upper) < 0)
            {
                return null;
            }

            buffer[length++] = upper;
        }

        return length == Length ? new string(buffer[..length]) : null;
    }
}
