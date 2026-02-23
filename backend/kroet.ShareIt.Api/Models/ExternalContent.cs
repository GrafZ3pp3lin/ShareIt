using System.Diagnostics.CodeAnalysis;

namespace kroet.ShareIt.Api.Models;

/// <summary>
/// Represents content that is stored externally.
/// </summary>
public sealed record ExternalContent : Api.Models.Content
{
    public ExternalContent() {}

    [SetsRequiredMembers]
    public ExternalContent(Api.Models.Content content, string url) : base(content.Id, content.PackageId, content.ContentType, content.CreatedAt, content.Size, content.UserId, content.MimeType, content.FileName)
    {
        Url = url;
    }
    
    /// <summary>
    /// URL where the external content is located.
    /// </summary>
    public required string Url { get; init; }
}