using kroet.ShareIt.Api.Models;
using ContentModel = kroet.ShareIt.Api.Models.Content;

namespace kroet.ShareIt.Api.Storage;

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
    Task StoreContentAsync(ContentModel content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a content package by ID.
    /// </summary>
    /// <param name="packageId">The unique package identifier.</param>
    /// <param name="includeContent">Whether to include content data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Package, or null if not found</returns>
    Task<ContentPackage?> GetPackageAsync(Guid packageId, bool includeContent = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves only the package shell — no items, no blob reads. Used to
    /// check the unlock proof before an access is claimed.
    /// </summary>
    /// <param name="packageId">The unique package identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Package without contents, or null if not found.</returns>
    Task<ContentPackage?> GetPackageShellAsync(Guid packageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the packages owned by a user, newest first, with item count and
    /// total stored size.
    /// </summary>
    /// <param name="userId">The authenticated owner.</param>
    /// <param name="cursor">Opaque cursor from a previous page, or null for the first page.</param>
    /// <param name="take">Maximum number of rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page and the cursor for the next page (null when done).</returns>
    Task<PackageListPage> ListPackagesAsync(Guid userId, string? cursor, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves content data by its ID.
    /// </summary>
    /// <param name="contentId">The unique content identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Content, or null if not found</returns>
    Task<ContentModel?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims one access and stores its session. The guarded update
    /// rejects the claim once the package's access limit has been reached, and
    /// the session insert rolls back with it when either step fails.
    /// </summary>
    /// <param name="session">The session to create; its creation time is also the access time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the claim operation.</returns>
    Task<AccessSessionClaimResult> CreateAccessSessionAsync(AccessSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an access session by its token hash. Returns null when no session
    /// has that hash.
    /// </summary>
    Task<AccessSession?> GetAccessSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes sessions whose expiry is at or before the given time.
    /// </summary>
    /// <returns>Number of sessions deleted.</returns>
    Task<long> DeleteExpiredAccessSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes multiple packages and their content, queueing their blobs in
    /// the same transaction.
    /// </summary>
    /// <remarks>
    /// Low-level metadata and queue primitive. Production callers must use
    /// <see cref="Services.Storage.IStorageHandler"/> so queue draining is
    /// owned by the cleanup service; direct calls leave entries queued.
    /// </remarks>
    /// <param name="packageIds">The unique package identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of packages deleted.</returns>
    Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a package and its content, queueing its blobs in the same
    /// transaction.
    /// </summary>
    /// <remarks>
    /// Low-level metadata and queue primitive. Production callers must use
    /// <see cref="Services.Storage.IStorageHandler"/> so queue draining is
    /// owned by the cleanup service; direct calls leave entries queued.
    /// </remarks>
    /// <param name="packageId">The unique package identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets expired packages and exhausted packages whose download-retention
    /// grace period has elapsed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of package IDs to delete.</returns>
    Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets object keys whose metadata has been deleted but whose blob deletion
    /// still needs to be completed or retried.
    /// </summary>
    Task<IReadOnlyList<string>> GetPendingBlobDeletionsAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a queued blob deletion after the object is gone. Repeating
    /// this operation is safe.
    /// </summary>
    Task CompleteBlobDeletionAsync(string blobId, CancellationToken cancellationToken = default);
}
