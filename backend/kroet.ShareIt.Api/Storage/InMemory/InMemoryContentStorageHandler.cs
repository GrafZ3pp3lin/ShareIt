using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Models;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Storage.InMemory;

public class InMemoryContentStorageHandler : IContentStorageHandler
{
    private readonly List<ContentPackage> _packages = [];
    private readonly List<ExternalContent> _contents = [];
    private readonly List<string> _pendingBlobDeletions = [];
    private readonly TimeProvider _timeProvider;
    private readonly DownloadSettings _downloadSettings;

    public InMemoryContentStorageHandler(
        TimeProvider? timeProvider = null,
        IOptions<DownloadSettings>? downloadSettings = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _downloadSettings = downloadSettings?.Value ?? new DownloadSettings();
    }

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

    public Task<ContentPackage?> GetPackageShellAsync(Guid packageId, CancellationToken cancellationToken = default)
        => GetPackageAsync(packageId, includeContent: false, cancellationToken);

    public Task<PackageListPage> ListPackagesAsync(
        Guid userId,
        string? cursor,
        int take,
        CancellationToken cancellationToken = default)
    {
        var hasCursor = PackageCursor.TryDecode(cursor, out var cursorCreatedAt, out var cursorId);

        var rows = _packages
            .Where(package => package.UserId == userId)
            .Where(package => !hasCursor || IsBefore(package, cursorCreatedAt, cursorId))
            .OrderByDescending(package => package.CreatedAt)
            .ThenByDescending(package => package.Id.ToString("N"), StringComparer.Ordinal)
            .Take(take + 1)
            .ToList();

        string? nextCursor = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(rows.Count - 1);
            var last = rows[^1];
            nextCursor = PackageCursor.Encode(last.CreatedAt, last.Id);
        }

        var items = rows.Select(package =>
        {
            var contents = _contents.Where(content => content.PackageId == package.Id).ToList();
            return new ContentPackageSummary
            {
                Id = package.Id,
                CreatedAt = package.CreatedAt,
                ExpiresAt = package.ExpiresAt,
                ItemCount = contents.Count,
                TotalSize = contents.Sum(content => content.Size),
                MaxDownloadCount = package.MaxDownloadCount,
                CurrentDownloadCount = package.CurrentDownloadCount,
                IsProtected = package.UnlockVerifier is not null
            };
        }).ToList();

        return Task.FromResult(new PackageListPage(items, nextCursor));
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

        // Guarded claim: never consume accesses beyond the configured limit.
        if (package.IsAccessLimitReached)
        {
            return Task.FromResult(new DownloadIncrementResult(false, package.CurrentDownloadCount, package.MaxDownloadCount, true));
        }

        var newCount = package.CurrentDownloadCount + 1;
        _packages[index] = package with
        {
            CurrentDownloadCount = newCount,
            LastAccessAt = _timeProvider.GetUtcNow()
        };

        var limitReached = package.MaxDownloadCount.HasValue && newCount >= package.MaxDownloadCount.Value;
        return Task.FromResult(new DownloadIncrementResult(true, newCount, package.MaxDownloadCount, limitReached));
    }

    public Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
    {
        var requestedIds = packageIds.ToHashSet();
        var deletedIds = _packages
            .Where(package => requestedIds.Contains(package.Id))
            .Select(package => package.Id)
            .ToHashSet();

        QueueBlobDeletions(_contents.Where(content => deletedIds.Contains(content.PackageId)));

        var removed = _packages.RemoveAll(package => deletedIds.Contains(package.Id));

        // Match the PostgreSQL ON DELETE CASCADE behaviour: package deletion
        // also removes the package's contents.
        if (removed > 0)
        {
            _contents.RemoveAll(content => deletedIds.Contains(content.PackageId));
        }

        return Task.FromResult(removed);
    }

    public Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var package = _packages.FirstOrDefault(p => p.Id == packageId);
        if (package is null || !_packages.Remove(package))
        {
            return Task.FromResult(false);
        }

        QueueBlobDeletions(_contents.Where(content => content.PackageId == packageId));

        // Match the PostgreSQL ON DELETE CASCADE behaviour: package deletion
        // also removes the package's contents.
        _contents.RemoveAll(content => content.PackageId == packageId);
        return Task.FromResult(true);
    }

    public Task<int> DeleteContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default)
    {
        var requestedIds = contentIds.ToHashSet();
        QueueBlobDeletions(_contents.Where(content => requestedIds.Contains(content.Id)));
        var removed = _contents.RemoveAll(c => requestedIds.Contains(c.Id));
        return Task.FromResult(removed);
    }

    public Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        var content = _contents.FirstOrDefault(c => c.Id == contentId);
        if (content is null || !_contents.Remove(content))
        {
            return Task.FromResult(false);
        }

        QueueBlobDeletions([content]);
        return Task.FromResult(true);
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
        var now = _timeProvider.GetUtcNow();
        var exhaustedBefore = now.AddMinutes(-_downloadSettings.ExhaustedPackageRetentionMinutes);
        var packageIds = _packages
            .Where(package => package.IsExpired(now) || IsExhaustedBeyondRetention(package, exhaustedBefore))
            .Select(package => package.Id)
            .ToList();
        return Task.FromResult<IReadOnlyList<Guid>>(packageIds);
    }

    public Task<IReadOnlyList<string>> GetPendingBlobDeletionsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var blobIds = _pendingBlobDeletions.Take(Math.Clamp(take, 1, 1000)).ToList();
        return Task.FromResult<IReadOnlyList<string>>(blobIds);
    }

    public Task CompleteBlobDeletionAsync(string blobId, CancellationToken cancellationToken = default)
    {
        _pendingBlobDeletions.Remove(blobId);
        return Task.CompletedTask;
    }

    private static bool IsExhaustedBeyondRetention(ContentPackage package, DateTimeOffset exhaustedBefore)
        => package.IsAccessLimitReached
           && package.LastAccessAt is { } lastAccessAt
           && lastAccessAt <= exhaustedBefore;

    private void QueueBlobDeletions(IEnumerable<ExternalContent> contents)
    {
        foreach (var blobId in contents.Select(content => content.Url).Distinct(StringComparer.Ordinal))
        {
            if (!_pendingBlobDeletions.Contains(blobId, StringComparer.Ordinal))
            {
                _pendingBlobDeletions.Add(blobId);
            }
        }
    }

    /// <summary>
    /// Lexicographic <c>(created_at, id)</c> ordering, matching the SQL
    /// row comparison for the keyset cursor. GUIDs compare by their
    /// hyphen-free hex text, i.e. byte order — not <see cref="Guid.CompareTo"/>.
    /// </summary>
    private static bool IsBefore(ContentPackage package, DateTimeOffset createdAt, Guid id)
    {
        var createdComparison = package.CreatedAt.CompareTo(createdAt);
        if (createdComparison != 0)
        {
            return createdComparison < 0;
        }

        return string.CompareOrdinal(package.Id.ToString("N"), id.ToString("N")) < 0;
    }
}