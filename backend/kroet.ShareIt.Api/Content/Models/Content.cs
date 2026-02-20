using System.Diagnostics.CodeAnalysis;

namespace kroet.ShareIt.Api.Content.Models;

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
    public Content(Guid id, Guid packageId, ContentType contentType, DateTimeOffset createdAt, long size, Guid? userId, string mimeType, string fileName)
    {
        Id = id;
        PackageId = packageId;
        ContentType = contentType;
        CreatedAt = createdAt;
        Size = size;
        UserId = userId;
        MimeType = mimeType;
        FileName = fileName;
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
    /// Size of the content in bytes.
    /// </summary>
    /// <remarks>
    /// For file content, this is the file size. For text content, this is the UTF-8 byte length.
    /// </remarks>
    public required long Size { get; init; }

    /// <summary>
    /// Optional identifier of the user who created the content.
    /// </summary>
    /// <remarks>
    /// Null indicates anonymous content.
    /// </remarks>
    public Guid? UserId { get; init; }

    /// <summary>
    /// MIME type of the file (e.g., "image/png", "application/pdf", "text/plain").
    /// </summary>
    public string MimeType { get; init; } = string.Empty;
    
    /// <summary>
    /// Original name of the uploaded file or auto-generated name for text content.
    /// </summary>
    public string FileName { get; init; } = string.Empty;
}
