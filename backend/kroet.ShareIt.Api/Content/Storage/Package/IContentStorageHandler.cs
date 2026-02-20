using kroet.ShareIt.Api.Content.Models;

namespace kroet.ShareIt.Api.Content.Storage.Package;

/// <summary>
/// Storage handler for content packages.
/// </summary>
public interface IContentStorageHandler
{
    /// <summary>
    /// Stores a content package.
    /// </summary>
    /// <param name="package">The package to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StorePackageAsync(ContentPackage package, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stores content.
    /// </summary>
    /// <param name="content">The content metadata to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreContentAsync(ExternalContent content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stores multiple contents.
    /// </summary>
    /// <param name="contents">The contents to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreContentsAsync(IEnumerable<ExternalContent> contents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a content package by ID.
    /// </summary>
    /// <param name="packageId">The unique package identifier.</param>
    /// <param name="includeContent">Whether to include content data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Package, or null if not found</returns>
    Task<ContentPackage?> GetPackageAsync(Guid packageId, bool includeContent = true, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves multiple content packages by their IDs.
    /// </summary>
    /// <param name="packageIds">The unique package identifiers.</param>
    /// <param name="includeContent">Whether to include content data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of packages.</returns>
    Task<IReadOnlyList<ContentPackage>> GetPackagesAsync(IEnumerable<Guid> packageIds, bool includeContent = true, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves content data by its ID.
    /// </summary>
    /// <param name="contentId">The unique content identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Content, or null if not found</returns>
    Task<ExternalContent?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves multiple contents by their IDs.
    /// </summary>
    /// <param name="contentIds">The unique content identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of contents.</returns>
    Task<IReadOnlyList<ExternalContent>> GetContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments the access count for a package.
    /// </summary>
    /// <param name="packageId">The unique package identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the increment operation.</returns>
    Task<DownloadIncrementResult> IncrementPackageAccessCountAsync(Guid packageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes multiple packages and their content.
    /// </summary>
    /// <param name="packageIds">The unique package identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of packages deleted.</returns>
    Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a package its content.
    /// </summary>
    /// <param name="packageId">The unique package identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes multiple contents.
    /// </summary>
    /// <param name="contentIds">The unique content identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of contents deleted.</returns>
    Task<int> DeleteContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes content and its associated data.
    /// </summary>
    /// <param name="contentId">The unique content identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if package exists.
    /// </summary>
    /// <param name="packageId">The unique package identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the package exists, false otherwise.</returns>
    Task<bool> ExistsPackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if content exists.
    /// </summary>
    /// <param name="contentId">The unique content identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the content exists, false otherwise.</returns>
    Task<bool> ExistsContentAsync(Guid contentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all packages that have reached their access limit.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of package IDs to delete.</returns>
    Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default);
}