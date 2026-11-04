using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Delegates all content-storage operations to the real handler, with an
/// optional override for <see cref="DeletePackageAsync"/> failure tests.
/// </summary>
internal sealed class DelegatingContentStorageHandler(
    IContentStorageHandler inner,
    Func<Guid, CancellationToken, Task<bool>>? deletePackage = null) : IContentStorageHandler
{
    public Task StorePackageAsync(ContentPackage package, CancellationToken cancellationToken = default)
        => inner.StorePackageAsync(package, cancellationToken);

    public Task StoreContentAsync(ExternalContent content, CancellationToken cancellationToken = default)
        => inner.StoreContentAsync(content, cancellationToken);

    public Task StoreContentsAsync(IEnumerable<ExternalContent> contents, CancellationToken cancellationToken = default)
        => inner.StoreContentsAsync(contents, cancellationToken);

    public Task<ContentPackage?> GetPackageAsync(
        Guid packageId,
        bool includeContent = true,
        CancellationToken cancellationToken = default)
        => inner.GetPackageAsync(packageId, includeContent, cancellationToken);

    public Task<ContentPackage?> GetPackageShellAsync(Guid packageId, CancellationToken cancellationToken = default)
        => inner.GetPackageShellAsync(packageId, cancellationToken);

    public Task<PackageListPage> ListPackagesAsync(
        Guid userId,
        string? cursor,
        int take,
        CancellationToken cancellationToken = default)
        => inner.ListPackagesAsync(userId, cursor, take, cancellationToken);

    public Task<IReadOnlyList<ContentPackage>> GetPackagesAsync(
        IEnumerable<Guid> packageIds,
        bool includeContent = true,
        CancellationToken cancellationToken = default)
        => inner.GetPackagesAsync(packageIds, includeContent, cancellationToken);

    public Task<ExternalContent?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default)
        => inner.GetContentAsync(contentId, cancellationToken);

    public Task<IReadOnlyList<ExternalContent>> GetContentsAsync(
        IEnumerable<Guid> contentIds,
        CancellationToken cancellationToken = default)
        => inner.GetContentsAsync(contentIds, cancellationToken);

    public Task<DownloadIncrementResult> IncrementPackageAccessCountAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
        => inner.IncrementPackageAccessCountAsync(packageId, cancellationToken);

    public Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
        => inner.DeletePackagesAsync(packageIds, cancellationToken);

    public Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        => deletePackage is null
            ? inner.DeletePackageAsync(packageId, cancellationToken)
            : deletePackage(packageId, cancellationToken);

    public Task<int> DeleteContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default)
        => inner.DeleteContentsAsync(contentIds, cancellationToken);

    public Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default)
        => inner.DeleteContentAsync(contentId, cancellationToken);

    public Task<bool> ExistsPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        => inner.ExistsPackageAsync(packageId, cancellationToken);

    public Task<bool> ExistsContentAsync(Guid contentId, CancellationToken cancellationToken = default)
        => inner.ExistsContentAsync(contentId, cancellationToken);

    public Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default)
        => inner.GetPackagesToDeleteAsync(cancellationToken);
}
