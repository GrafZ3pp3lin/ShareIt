using System.Buffers.Text;
using System.Text;

namespace kroet.ShareIt.Api.Storage;

/// <summary>
/// Opaque, URL-safe cursor for the package list: base64url of
/// <c>(created_at ticks, id)</c>. Ordering is <c>created_at DESC, id DESC</c>,
/// so the cursor is the last row of the previous page.
/// </summary>
public static class PackageCursor
{
    /// <summary>
    /// Encodes a row position into a cursor string.
    /// </summary>
    public static string Encode(DateTimeOffset createdAt, Guid id)
    {
        var value = $"{createdAt.UtcDateTime.Ticks}:{id:N}";
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Decodes a cursor, returning false for anything malformed.
    /// </summary>
    public static bool TryDecode(string? cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var parts = decoded.Split(':');
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
