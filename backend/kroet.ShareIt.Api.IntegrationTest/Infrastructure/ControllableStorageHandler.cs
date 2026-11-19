using kroet.ShareIt.Api.Services.Storage;
using ContentModel = kroet.ShareIt.Api.Models.Content;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Delegates to the real storage handler but can fail a configurable
/// <see cref="StoreContentAsync"/> call to exercise endpoint rollback logic.
/// </summary>
internal sealed class ControllableStorageHandler(IStorageHandler inner) : IStorageHandler
{
    private int _storeCallCount;

    public int? FailOnStoreCall { get; set; }

    public int StoreCallCount => Volatile.Read(ref _storeCallCount);

    public Task StoreContentAsync(
        ContentModel content,
        Stream dataStream,
        CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _storeCallCount);

        if (FailOnStoreCall == call)
        {
            throw new InvalidOperationException("Simulated storage failure.");
        }

        return inner.StoreContentAsync(content, dataStream, cancellationToken);
    }

    public Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        => inner.DeletePackageAsync(packageId, cancellationToken);

    public Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
        => inner.DeletePackagesAsync(packageIds, cancellationToken);

    public Task<int> ProcessPendingBlobDeletionsAsync(CancellationToken cancellationToken = default)
        => inner.ProcessPendingBlobDeletionsAsync(cancellationToken);
}
