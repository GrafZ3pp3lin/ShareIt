using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;
using ContentModel = kroet.ShareIt.Api.Models.Content;

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

    public Task StoreContentAsync(ContentModel content, CancellationToken cancellationToken = default)
        => inner.StoreContentAsync(content, cancellationToken);

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

    public Task<ContentModel?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default)
        => inner.GetContentAsync(contentId, cancellationToken);

    public Task<AccessSessionClaimResult> CreateAccessSessionAsync(
        AccessSession session,
        CancellationToken cancellationToken = default)
        => inner.CreateAccessSessionAsync(session, cancellationToken);

    public Task<AccessSession?> GetAccessSessionByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => inner.GetAccessSessionByTokenHashAsync(tokenHash, cancellationToken);

    public Task<long> DeleteExpiredAccessSessionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => inner.DeleteExpiredAccessSessionsAsync(now, cancellationToken);

    public Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
        => inner.DeletePackagesAsync(packageIds, cancellationToken);

    public Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        => deletePackage is null
            ? inner.DeletePackageAsync(packageId, cancellationToken)
            : deletePackage(packageId, cancellationToken);

    public Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default)
        => inner.GetPackagesToDeleteAsync(cancellationToken);

    public Task<IReadOnlyList<string>> GetPendingBlobDeletionsAsync(
        int take,
        CancellationToken cancellationToken = default)
        => inner.GetPendingBlobDeletionsAsync(take, cancellationToken);

    public Task CompleteBlobDeletionAsync(string blobId, CancellationToken cancellationToken = default)
        => inner.CompleteBlobDeletionAsync(blobId, cancellationToken);
}
