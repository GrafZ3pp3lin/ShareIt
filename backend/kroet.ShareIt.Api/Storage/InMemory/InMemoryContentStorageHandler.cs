using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Models;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Storage.InMemory;

public class InMemoryContentStorageHandler : IContentStorageHandler
{
    private readonly List<ContentPackage> _packages = [];
    private readonly List<ExternalContent> _contents = [];
    private readonly List<AccessSession> _accessSessions = [];
    private readonly List<string> _pendingBlobDeletions = [];
    private readonly object _lock = new();
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
        lock (_lock)
        {
            _packages.Add(package);
        }

        return Task.CompletedTask;
    }

    public Task StoreContentAsync(ExternalContent content, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _contents.Add(content);
        }

        return Task.CompletedTask;
    }

    public Task StoreContentsAsync(IEnumerable<ExternalContent> contents, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _contents.AddRange(contents);
        }

        return Task.CompletedTask;
    }

    public Task<ContentPackage?> GetPackageAsync(Guid packageId, bool includeContent = true, CancellationToken cancellationToken = default)
    {
        lock (_lock)
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
    }

    public Task<ContentPackage?> GetPackageShellAsync(Guid packageId, CancellationToken cancellationToken = default)
        => GetPackageAsync(packageId, includeContent: false, cancellationToken);

    public Task<PackageListPage> ListPackagesAsync(
        Guid userId,
        string? cursor,
        int take,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
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
                    MaxAccessCount = package.MaxAccessCount,
                    CurrentAccessCount = package.CurrentAccessCount,
                    IsProtected = package.UnlockVerifier is not null
                };
            }).ToList();

            return Task.FromResult(new PackageListPage(items, nextCursor));
        }
    }

    public Task<IReadOnlyList<ContentPackage>> GetPackagesAsync(IEnumerable<Guid> packageIds, bool includeContent = true, CancellationToken cancellationToken = default)
    {
        lock (_lock)
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
    }

    public Task<ExternalContent?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var content = _contents.FirstOrDefault(c => c.Id == contentId);
            return Task.FromResult<ExternalContent?>(content);
        }
    }

    public Task<IReadOnlyList<ExternalContent>> GetContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var contents = _contents.Where(c => contentIds.Contains(c.Id)).ToList();
            return Task.FromResult<IReadOnlyList<ExternalContent>>(contents);
        }
    }

    public Task<AccessSessionClaimResult> CreateAccessSessionAsync(
        AccessSession session,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _packages.FindIndex(p => p.Id == session.PackageId);
            if (index < 0)
            {
                return Task.FromResult(new AccessSessionClaimResult(false, 0, null));
            }

            var package = _packages[index];

            // Guarded claim: never consume accesses for an expired package or
            // beyond the configured limit.
            if (package.IsExpired(session.CreatedAt) || package.IsAccessLimitReached)
            {
                return Task.FromResult(new AccessSessionClaimResult(false, 0, null));
            }

            var newCount = package.CurrentAccessCount + 1;
            _packages[index] = package with
            {
                CurrentAccessCount = newCount,
                LastAccessAt = session.CreatedAt
            };
            _accessSessions.Add(session);

            return Task.FromResult(new AccessSessionClaimResult(true, newCount, package.MaxAccessCount));
        }
    }

    public Task<AccessSession?> GetAccessSessionByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var session = _accessSessions.FirstOrDefault(
                candidate => string.Equals(candidate.TokenHash, tokenHash, StringComparison.Ordinal));
            return Task.FromResult(session);
        }
    }

    public Task<long> DeleteExpiredAccessSessionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var removed = (long)_accessSessions.RemoveAll(session => session.ExpiresAt <= now);
            return Task.FromResult(removed);
        }
    }

    public Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var requestedIds = packageIds.ToHashSet();
            var deletedIds = _packages
                .Where(package => requestedIds.Contains(package.Id))
                .Select(package => package.Id)
                .ToHashSet();

            QueueBlobDeletions(_contents.Where(content => deletedIds.Contains(content.PackageId)));

            var removed = _packages.RemoveAll(package => deletedIds.Contains(package.Id));

            // Match the PostgreSQL ON DELETE CASCADE behaviour: package deletion
            // also removes the package's contents and access sessions.
            if (removed > 0)
            {
                _contents.RemoveAll(content => deletedIds.Contains(content.PackageId));
                _accessSessions.RemoveAll(session => deletedIds.Contains(session.PackageId));
            }

            return Task.FromResult(removed);
        }
    }

    public Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var package = _packages.FirstOrDefault(p => p.Id == packageId);
            if (package is null || !_packages.Remove(package))
            {
                return Task.FromResult(false);
            }

            QueueBlobDeletions(_contents.Where(content => content.PackageId == packageId));

            // Match the PostgreSQL ON DELETE CASCADE behaviour: package deletion
            // also removes the package's contents and access sessions.
            _contents.RemoveAll(content => content.PackageId == packageId);
            _accessSessions.RemoveAll(session => session.PackageId == packageId);
            return Task.FromResult(true);
        }
    }

    public Task<int> DeleteContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var requestedIds = contentIds.ToHashSet();
            QueueBlobDeletions(_contents.Where(content => requestedIds.Contains(content.Id)));
            var removed = _contents.RemoveAll(c => requestedIds.Contains(c.Id));
            return Task.FromResult(removed);
        }
    }

    public Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var content = _contents.FirstOrDefault(c => c.Id == contentId);
            if (content is null || !_contents.Remove(content))
            {
                return Task.FromResult(false);
            }

            QueueBlobDeletions([content]);
            return Task.FromResult(true);
        }
    }

    public Task<bool> ExistsPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var exists = _packages.Any(p => p.Id == packageId);
            return Task.FromResult(exists);
        }
    }

    public Task<bool> ExistsContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var exists = _contents.Any(c => c.Id == contentId);
            return Task.FromResult(exists);
        }
    }

    public Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();
            var exhaustedBefore = now.AddMinutes(-_downloadSettings.ExhaustedPackageRetentionMinutes);
            var packageIds = _packages
                .Where(package => package.IsExpired(now) || IsExhaustedBeyondRetention(package, exhaustedBefore))
                .Select(package => package.Id)
                .ToList();
            return Task.FromResult<IReadOnlyList<Guid>>(packageIds);
        }
    }

    public Task<IReadOnlyList<string>> GetPendingBlobDeletionsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var blobIds = _pendingBlobDeletions.Take(Math.Clamp(take, 1, 1000)).ToList();
            return Task.FromResult<IReadOnlyList<string>>(blobIds);
        }
    }

    public Task CompleteBlobDeletionAsync(string blobId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _pendingBlobDeletions.Remove(blobId);
        }

        return Task.CompletedTask;
    }

    private static bool IsExhaustedBeyondRetention(ContentPackage package, DateTimeOffset exhaustedBefore)
        => package.IsAccessLimitReached
           && package.LastAccessAt is { } lastAccessAt
           && lastAccessAt <= exhaustedBefore;

    // Callers hold _lock for the lifetime of this helper.
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
