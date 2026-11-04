using System.Diagnostics.CodeAnalysis;

namespace kroet.ShareIt.Api.Models;

/// <summary>
/// Defines the type of content being stored.
/// </summary>
public enum ContentType
{
    /// <summary>
    /// Plain text content.
    /// </summary>
    Text,

    /// <summary>
    /// Binary file content (e.g., documents, images, videos).
    /// </summary>
    File,
}

/// <summary>
/// Base for all content types in the system.
/// </summary>
public record Content
{
    public Content() {}

    [SetsRequiredMembers]
    public Content(Guid id, Guid packageId, ContentType contentType, DateTimeOffset createdAt, long size, Guid? userId, string? meta)
    {
        Id = id;
        PackageId = packageId;
        ContentType = contentType;
        CreatedAt = createdAt;
        Size = size;
        UserId = userId;
        Meta = meta;
    }

    /// <summary>
    /// Unique identifier for the content.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Identifier of the package this content belongs to.
    /// </summary>
    public required Guid PackageId { get; init; }

    /// <summary>
    /// The type of content (File or Text).
    /// </summary>
    public required ContentType ContentType { get; init; }

    /// <summary>
    /// Timestamp when the content was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Size of the content in bytes. This is the stored byte length for both
    /// text and file items — never inflated by base64 encoding.
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// Optional identifier of the user who created the content.
    /// </summary>
    /// <remarks>
    /// Null indicates anonymous content.
    /// </remarks>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Opaque per-item metadata blob (base64url), carrying the name and MIME
    /// type. It is sealed client-side when the package is protected. The
    /// server never parses or interprets it.
    /// </summary>
    public string? Meta { get; init; }
}
