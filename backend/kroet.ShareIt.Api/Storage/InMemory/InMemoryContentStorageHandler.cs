using kroet.ShareIt.Api.Models;

namespace kroet.ShareIt.Api.Storage.InMemory;

public class InMemoryContentStorageHandler : IContentStorageHandler
{
    private readonly List<ContentPackage> _packages = [];
    private readonly List<ExternalContent> _contents = [];

    public Task StorePackageAsync(ContentPackage package, CancellationToken cancellationToken = default)
    {
        _packages.Add(package);
        return Task.CompletedTask;
    }

    public Task StoreContentAsync(ExternalContent content, CancellationToken cancellationToken = default)
    {
        _contents.Add(content);
        return Task.CompletedTask;
    }

    public Task StoreContentsAsync(IEnumerable<ExternalContent> contents, CancellationToken cancellationToken = default)
    {
        _contents.AddRange(contents);
        return Task.CompletedTask;
    }

    public Task<ContentPackage?> GetPackageAsync(Guid packageId, bool includeContent = true, CancellationToken cancellationToken = default)
    {
        var package = _packages.FirstOrDefault(p => p.Id == packageId);
        if (package is null)
        {
            return Task.FromResult<ContentPackage?>(null);
        }

        if (includeContent)
        {
            var contents = _contents.Where(c => c.PackageId == packageId).ToList();
            var packageWithContent = package with { Contents = contents };
            return Task.FromResult<ContentPackage?>(packageWithContent);
        }

        return Task.FromResult<ContentPackage?>(package);
    }

    public Task<IReadOnlyList<ContentPackage>> GetPackagesAsync(IEnumerable<Guid> packageIds, bool includeContent = true, CancellationToken cancellationToken = default)
    {
        var packages = _packages.Where(p => packageIds.Contains(p.Id)).ToList();

        if (includeContent)
        {
            var contents = _contents.Where(c => packageIds.Contains(c.PackageId)).ToList();
            var packagesWithContent = packages.Select(p =>
            {
                var packageContents = contents.Where(c => c.PackageId == p.Id).ToList();
                return p with { Contents = packageContents };
            }).ToList();

            return Task.FromResult<IReadOnlyList<ContentPackage>>(packagesWithContent);
        }

        return Task.FromResult<IReadOnlyList<ContentPackage>>(packages);
    }

    public Task<ExternalContent?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        var content = _contents.FirstOrDefault(c => c.Id == contentId);
        return Task.FromResult<ExternalContent?>(content);
    }

    public Task<IReadOnlyList<ExternalContent>> GetContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default)
    {
        var contents = _contents.Where(c => contentIds.Contains(c.Id)).ToList();
        return Task.FromResult<IReadOnlyList<ExternalContent>>(contents);
    }

    public Task<DownloadIncrementResult> IncrementPackageAccessCountAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var index = _packages.FindIndex(p => p.Id == packageId);
        if (index < 0)
        {
            return Task.FromResult(new DownloadIncrementResult(false, 0, null, false));
        }

        var package = _packages[index];
        var newCount = package.CurrentDownloadCount + 1;
        _packages[index] = package with { CurrentDownloadCount = newCount };

        var limitReached = package.MaxDownloadCount.HasValue && newCount >= package.MaxDownloadCount.Value;
        return Task.FromResult(new DownloadIncrementResult(true, newCount, package.MaxDownloadCount, limitReached));
    }

    public Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
    {
        var removed = _packages.RemoveAll(p => packageIds.Contains(p.Id));
        return Task.FromResult(removed);
    }

    public Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var package = _packages.FirstOrDefault(p => p.Id == packageId);
        return Task.FromResult(package is not null && _packages.Remove(package));
    }

    public Task<int> DeleteContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default)
    {
        var removed = _contents.RemoveAll(c => contentIds.Contains(c.Id));
        return Task.FromResult(removed);
    }

    public Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        var content = _contents.FirstOrDefault(c => c.Id == contentId);
        return Task.FromResult(content is not null && _contents.Remove(content));
    }

    public Task<bool> ExistsPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var exists = _packages.Any(p => p.Id == packageId);
        return Task.FromResult(exists);
    }

    public Task<bool> ExistsContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        var exists = _contents.Any(c => c.Id == contentId);
        return Task.FromResult(exists);
    }

    public Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default)
    {
        var packageIds = _packages.Select(p => p.Id).ToList();
        return Task.FromResult<IReadOnlyList<Guid>>(packageIds);
    }
}