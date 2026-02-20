namespace kroet.ShareIt.Api.Content.Models;

/// <summary>
/// Result of incrementing download count.
/// </summary>
public record DownloadIncrementResult(
    bool Success,
    int NewCount,
    int? MaxCount,
    bool LimitReached
);