using Dapper;
using ContentModel = kroet.ShareIt.Api.Models.Content;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace kroet.ShareIt.Api.Storage.Postgres;

/// <summary>
/// PostgreSQL implementation of content package storage using Dapper.
/// </summary>
public class SqlContentStorageHandler : IContentStorageHandler, IMetricsSnapshotProvider
{
    static SqlContentStorageHandler()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private readonly SqlStorageSettings _settings;
    private readonly ILogger<SqlContentStorageHandler> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly DownloadSettings _downloadSettings;

    public SqlContentStorageHandler(
        IOptions<SqlStorageSettings> settings,
        ILogger<SqlContentStorageHandler> logger,
        TimeProvider? timeProvider = null,
        IOptions<DownloadSettings>? downloadSettings = null)
    {
        _settings = settings.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _downloadSettings = downloadSettings?.Value ?? new DownloadSettings();
    }

    /// <inheritdoc />
    public async Task StorePackageAsync(ContentPackage package, CancellationToken cancellationToken = default)
    {
        const string insertPackageSql = """
                                        INSERT INTO content_packages (
                                            id, created_at, user_id,
                                            max_access_count, current_access_count, last_access_at,
                                            expires_at, unlock_verifier
                                        ) VALUES (
                                            @Id, @CreatedAt, @UserId,
                                            @MaxAccessCount, 0, @LastAccessAt,
                                            @ExpiresAt, @UnlockVerifier
                                        );
                                        """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            insertPackageSql,
            new
            {
                package.Id,
                package.CreatedAt,
                package.UserId,
                package.MaxAccessCount,
                package.LastAccessAt,
                package.ExpiresAt,
                package.UnlockVerifier
            });
    }

    /// <inheritdoc />
    public async Task StoreContentAsync(ContentModel content, CancellationToken cancellationToken = default)
    {
        const string insertSql = """
                                 INSERT INTO contents (
                                     id, package_id, content_type, created_at, size, user_id,
                                     meta, url
                                 ) VALUES (
                                     @Id, @PackageId, @ContentType, @CreatedAt, @Size, @UserId,
                                     @Meta, @Url
                                 );
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            insertSql,
            new
            {
                content.Id,
                content.PackageId,
                ContentType = content.ContentType.ToString(),
                content.CreatedAt,
                content.Size,
                content.UserId,
                content.Meta,
                content.Url
            });
    }

    /// <inheritdoc />
    public async Task<ContentPackage?> GetPackageAsync(Guid packageId, bool includeContent = true, CancellationToken cancellationToken = default)
    {
        const string getPackageSql = """
                                     SELECT
                                         id, created_at, user_id,
                                         max_access_count, current_access_count, last_access_at, expires_at,
                                         unlock_verifier
                                     FROM content_packages
                                     WHERE id = @PackageId
                                     """;
        
        const string getContentSql = """
                                        SELECT
                                            id, package_id, content_type, created_at, size, user_id,
                                            meta, url
                                        FROM contents
                                        WHERE package_id = @PackageId
                                        ORDER BY created_at, id
                                        """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Get package metadata
        var packageData = await connection.QuerySingleOrDefaultAsync<PackageDbDto>(
            getPackageSql,
            new { PackageId = packageId });

        if (packageData == null)
            return null;

        var package = MapToContentPackage(packageData);

        // If includeContent is true, fetch the full content objects
        if (includeContent)
        {
            var contentDtoList = await connection.QueryAsync<ContentDbDto>(
                getContentSql,
                new { PackageId = packageId });
            package.Contents = [.. contentDtoList.Select(MapToContent)];
        }

        return package;
    }

    /// <inheritdoc />
    public Task<ContentPackage?> GetPackageShellAsync(Guid packageId, CancellationToken cancellationToken = default)
        => GetPackageAsync(packageId, includeContent: false, cancellationToken);

    /// <inheritdoc />
    public async Task<PackageListPage> ListPackagesAsync(
        Guid userId,
        string? cursor,
        int take,
        CancellationToken cancellationToken = default)
    {
        // A malformed cursor is not an error the client can fix; treat it as
        // the first page instead of leaking a 500.
        var hasCursor = PackageCursor.TryDecode(cursor, out var cursorCreatedAt, out var cursorId);

        const string listSql = """
                               SELECT
                                   p.id, p.created_at, p.expires_at, p.max_access_count, p.current_access_count,
                                   p.unlock_verifier,
                                   (SELECT COUNT(*)::int FROM contents c WHERE c.package_id = p.id) AS item_count,
                                   (SELECT COALESCE(SUM(c.size), 0)::bigint FROM contents c WHERE c.package_id = p.id) AS total_size
                               FROM content_packages p
                               WHERE p.user_id = @UserId
                                 AND (@hasCursor = false OR (p.created_at, p.id) < (@CursorCreatedAt, @CursorId))
                               ORDER BY p.created_at DESC, p.id DESC
                               LIMIT @Limit
                               """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Fetch one extra row to know whether another page exists.
        var rows = (await connection.QueryAsync<PackageSummaryDbDto>(listSql, new
        {
            UserId = userId,
            hasCursor,
            CursorCreatedAt = cursorCreatedAt,
            CursorId = cursorId,
            Limit = take + 1
        })).ToList();

        string? nextCursor = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(rows.Count - 1);
            var last = rows[^1];
            nextCursor = PackageCursor.Encode(last.CreatedAt, last.Id);
        }

        var items = rows.Select(row => new ContentPackageSummary
        {
            Id = row.Id,
            CreatedAt = row.CreatedAt,
            ExpiresAt = row.ExpiresAt,
            ItemCount = row.ItemCount,
            TotalSize = row.TotalSize,
            MaxAccessCount = row.MaxAccessCount,
            CurrentAccessCount = row.CurrentAccessCount,
            IsProtected = row.UnlockVerifier is not null
        }).ToList();

        return new PackageListPage(items, nextCursor);
    }

    /// <inheritdoc />
    public async Task<ContentModel?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        const string selectSql = """
                                 SELECT
                                     id, package_id, content_type, created_at, size, user_id,
                                     meta, url
                                 FROM contents
                                 WHERE id = @ContentId
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var dto = await connection.QuerySingleOrDefaultAsync<ContentDbDto>(
            selectSql,
            new { ContentId = contentId });

        return dto is null ? null : MapToContent(dto);
    }

    /// <inheritdoc />
    public async Task<AccessSessionClaimResult> CreateAccessSessionAsync(
        AccessSession session,
        CancellationToken cancellationToken = default)
    {
        // Atomic claim of one access plus its session: the guarded UPDATE
        // serializes concurrent claims on the package row, and the insert
        // rolls back if the session cannot be stored. The request path only
        // reaches this after all response preparation has succeeded.
        const string incrementSql = """
                                    UPDATE content_packages
                                        SET current_access_count = current_access_count + 1,
                                            last_access_at = @AccessedAt
                                    WHERE id = @PackageId
                                      AND (max_access_count IS NULL OR current_access_count < max_access_count)
                                      AND expires_at > @AccessedAt
                                    RETURNING current_access_count, max_access_count
                                    """;
        const string insertSessionSql = """
                                        INSERT INTO access_sessions (
                                            id, package_id, client_id, is_guest, token_hash,
                                            created_at, expires_at
                                        ) VALUES (
                                            @Id, @PackageId, @ClientId, @IsGuest, @TokenHash,
                                            @CreatedAt, @ExpiresAt
                                        )
                                        """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var claim = await connection.QuerySingleOrDefaultAsync<AccessCountDbDto>(
            new CommandDefinition(
                incrementSql,
                new { session.PackageId, AccessedAt = session.CreatedAt },
                transaction,
                cancellationToken: cancellationToken));

        if (claim == null)
        {
            _logger.LogWarning("Failed to claim an access - package {PackageId} not found, expired, or exhausted", session.PackageId);
            return new AccessSessionClaimResult(false, 0, null);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            insertSessionSql,
            new
            {
                session.Id,
                session.PackageId,
                session.ClientId,
                session.IsGuest,
                session.TokenHash,
                session.CreatedAt,
                session.ExpiresAt
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return new AccessSessionClaimResult(
            true,
            claim.CurrentAccessCount,
            claim.MaxAccessCount);
    }

    /// <inheritdoc />
    public async Task<AccessSession?> GetAccessSessionByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        const string selectSql = """
                                 SELECT id, package_id, client_id, is_guest, token_hash,
                                        created_at, expires_at
                                 FROM access_sessions
                                 WHERE token_hash = @TokenHash
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var dto = await connection.QuerySingleOrDefaultAsync<AccessSessionDbDto>(
            new CommandDefinition(selectSql, new { TokenHash = tokenHash }, cancellationToken: cancellationToken));

        return dto is null ? null : MapToAccessSession(dto);
    }

    /// <inheritdoc />
    public async Task<long> DeleteExpiredAccessSessionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string deleteSql = "DELETE FROM access_sessions WHERE expires_at <= @Now";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteAsync(
            new CommandDefinition(deleteSql, new { Now = now }, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        const string lockPackageSql = "SELECT id FROM content_packages WHERE id = @PackageId FOR UPDATE";
        const string queueBlobDeletionsSql = """
                                             INSERT INTO blob_deletion_jobs (blob_id, created_at)
                                             SELECT url, @CreatedAt
                                             FROM contents
                                             WHERE package_id = @PackageId
                                             ON CONFLICT (blob_id) DO NOTHING
                                             """;
        const string deletePackageSql = "DELETE FROM content_packages WHERE id = @PackageId";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var parameters = new { PackageId = packageId, CreatedAt = _timeProvider.GetUtcNow() };

        // Lock the package row first so a concurrent upload cannot commit its
        // contents row between queueing and the cascade delete: either the
        // upload's FK check completes before the lock is acquired (the queue
        // then sees the row) or it fails afterward and its compensation
        // removes the blob.
        await connection.ExecuteAsync(lockPackageSql, parameters, transaction);
        await connection.ExecuteAsync(queueBlobDeletionsSql, parameters, transaction);
        var affected = await connection.ExecuteAsync(deletePackageSql, parameters, transaction);
        await transaction.CommitAsync(cancellationToken);

        var deleted = affected > 0;

        return deleted;
    }

    /// <inheritdoc />
    public async Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
    {
        var packageIdList = packageIds.ToList();
        if (packageIdList.Count == 0)
        {
            return 0;
        }

        // A deterministic lock order prevents overlapping cleanup deletions
        // from deadlocking on the same package rows.
        const string lockPackagesSql = """
                                       SELECT id
                                       FROM content_packages
                                       WHERE id = ANY(@PackageIds)
                                       ORDER BY id
                                       FOR UPDATE
                                       """;
        const string queueBlobDeletionsSql = """
                                             INSERT INTO blob_deletion_jobs (blob_id, created_at)
                                             SELECT url, @CreatedAt
                                             FROM contents
                                             WHERE package_id = ANY(@PackageIds)
                                             ON CONFLICT (blob_id) DO NOTHING
                                             """;
        const string deletePackagesSql = """
                                         DELETE FROM content_packages
                                         WHERE id = ANY(@PackageIds)
                                         """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var parameters = new { PackageIds = packageIdList.ToArray(), CreatedAt = _timeProvider.GetUtcNow() };

        // Lock the package rows first so concurrent uploads cannot commit
        // between queueing and the cascade delete (see DeletePackageAsync).
        await connection.ExecuteAsync(lockPackagesSql, parameters, transaction);
        await connection.ExecuteAsync(queueBlobDeletionsSql, parameters, transaction);
        var rowsAffected = await connection.ExecuteAsync(deletePackagesSql, parameters, transaction);
        await transaction.CommitAsync(cancellationToken);

        return rowsAffected;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default)
    {
        // Two independent branches let the planner use the expiry index and
        // the exhausted-retention index instead of scanning the table for the
        // combined OR predicate. UNION also removes duplicates for packages
        // that are both expired and exhausted.
        const string getExpiredPackagesSql = """
                                             SELECT id
                                             FROM content_packages
                                             WHERE expires_at <= @Now
                                             UNION
                                             SELECT id
                                             FROM content_packages
                                             WHERE max_access_count IS NOT NULL
                                               AND current_access_count >= max_access_count
                                               AND last_access_at <= @ExhaustedBefore
                                             """;

        var now = _timeProvider.GetUtcNow();
        var exhaustedBefore = now.AddMinutes(-_downloadSettings.ExhaustedPackageRetentionMinutes);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var packageIds = await connection.QueryAsync<Guid>(
            getExpiredPackagesSql,
            new { Now = now, ExhaustedBefore = exhaustedBefore });

        var result = packageIds.ToList();

        return result;
    }

    /// <inheritdoc />
    public async Task<ApplicationMetricsSnapshot> GetMetricsSnapshotAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string snapshotSql = """
                                   SELECT
                                       COUNT(*)::int AS total_packages,
                                       COUNT(*) FILTER (WHERE NOT (
                                           (expires_at <= @Now)
                                           OR (
                                               max_access_count IS NOT NULL
                                               AND current_access_count >= max_access_count
                                               AND last_access_at <= @ExhaustedBefore
                                           )
                                       ))::int AS active_packages,
                                       COUNT(*) FILTER (WHERE expires_at <= @Now)::int AS expired_packages,
                                       COUNT(*) FILTER (WHERE max_access_count IS NOT NULL AND current_access_count >= max_access_count)::int AS exhausted_packages,
                                       COUNT(*) FILTER (WHERE unlock_verifier IS NOT NULL)::int AS protected_packages,
                                       COUNT(*) FILTER (WHERE NOT EXISTS (
                                           SELECT 1 FROM contents c WHERE c.package_id = p.id
                                       ))::int AS empty_packages,
                                       (SELECT COUNT(*)::int FROM contents) AS total_items,
                                       (SELECT COALESCE(SUM(size), 0)::bigint FROM contents) AS total_bytes,
                                       (SELECT COUNT(*)::int FROM blob_deletion_jobs) AS pending_blob_deletions
                                   FROM content_packages p
                                   """;

        // A package exhausted within the retention window can still serve its
        // outstanding downloads, so it counts as active rather than as
        // cleanup-pending until retention elapses.
        var exhaustedBefore = now.AddMinutes(-_downloadSettings.ExhaustedPackageRetentionMinutes);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleAsync<MetricsSnapshotDbDto>(
            new CommandDefinition(snapshotSql, new { Now = now, ExhaustedBefore = exhaustedBefore }, cancellationToken: cancellationToken));

        return new ApplicationMetricsSnapshot
        {
            TotalPackages = row.TotalPackages,
            ActivePackages = row.ActivePackages,
            ExpiredPackages = row.ExpiredPackages,
            ExhaustedPackages = row.ExhaustedPackages,
            ProtectedPackages = row.ProtectedPackages,
            EmptyPackages = row.EmptyPackages,
            TotalItems = row.TotalItems,
            TotalBytes = row.TotalBytes,
            PendingBlobDeletions = row.PendingBlobDeletions,
            CleanupPending = row.TotalPackages - row.ActivePackages
        };
    }
    
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetPendingBlobDeletionsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        const string getPendingSql = """
                                     SELECT blob_id
                                     FROM blob_deletion_jobs
                                     ORDER BY created_at, blob_id
                                     LIMIT @Limit
                                     """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var blobIds = await connection.QueryAsync<string>(
            getPendingSql,
            new { Limit = Math.Clamp(take, 1, 1000) });
        return blobIds.AsList();
    }

    /// <inheritdoc />
    public async Task CompleteBlobDeletionAsync(string blobId, CancellationToken cancellationToken = default)
    {
        const string completeSql = "DELETE FROM blob_deletion_jobs WHERE blob_id = @BlobId";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(completeSql, new { BlobId = blobId });
    }

    private NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(_settings.ConnectionString);
    }

    /// <summary>
    /// Maps a DTO to the ContentModel domain model.
    /// </summary>
    private static ContentModel MapToContent(ContentDbDto dbDto)
    {
        return new ContentModel
        {
            Id = dbDto.Id,
            PackageId = dbDto.PackageId,
            ContentType = Enum.Parse<ContentType>(dbDto.ContentType),
            CreatedAt = dbDto.CreatedAt,
            Size = dbDto.Size,
            UserId = dbDto.UserId,
            Meta = dbDto.Meta,
            Url = dbDto.Url
        };
    }
    
    /// <summary>
    /// Maps a DTO to the AccessSession domain model.
    /// </summary>
    private static AccessSession MapToAccessSession(AccessSessionDbDto dbDto)
    {
        return new AccessSession
        {
            Id = dbDto.Id,
            PackageId = dbDto.PackageId,
            ClientId = dbDto.ClientId,
            IsGuest = dbDto.IsGuest,
            TokenHash = dbDto.TokenHash,
            CreatedAt = dbDto.CreatedAt,
            ExpiresAt = dbDto.ExpiresAt
        };
    }

    /// <summary>
    /// Maps a DTO to ContentPackage domain model.
    /// </summary>
    private static ContentPackage MapToContentPackage(PackageDbDto dbDto)
    {
        return new ContentPackage
        {
            Id = dbDto.Id,
            CreatedAt = dbDto.CreatedAt,
            UserId = dbDto.UserId,
            UnlockVerifier = dbDto.UnlockVerifier,
            MaxAccessCount = dbDto.MaxAccessCount,
            CurrentAccessCount = dbDto.CurrentAccessCount,
            LastAccessAt = dbDto.LastAccessAt,
            ExpiresAt = dbDto.ExpiresAt
        };
    }

    /// <summary>
    /// DTO for mapping package data from database.
    /// </summary>
    private sealed class PackageDbDto
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public Guid? UserId { get; init; }
        public int? MaxAccessCount { get; init; }
        public int CurrentAccessCount { get; init; }
        public DateTimeOffset? LastAccessAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public string? UnlockVerifier { get; init; }
    }

    /// <summary>
    /// DTO for mapping package list rows with their aggregate counts.
    /// </summary>
    private sealed class PackageSummaryDbDto
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public int ItemCount { get; init; }
        public long TotalSize { get; init; }
        public int? MaxAccessCount { get; init; }
        public int CurrentAccessCount { get; init; }
        public string? UnlockVerifier { get; init; }
    }

    /// <summary>
    /// DTO for mapping access count results.
    /// </summary>
    private sealed class AccessCountDbDto
    {
        public int CurrentAccessCount { get; init; }
        public int? MaxAccessCount { get; init; }
    }

    /// <summary>
    /// DTO for mapping access session rows.
    /// </summary>
    private sealed class AccessSessionDbDto
    {
        public Guid Id { get; init; }
        public Guid PackageId { get; init; }
        public Guid ClientId { get; init; }
        public bool IsGuest { get; init; }
        public string TokenHash { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    /// <summary>
    /// DTO for mapping application metrics aggregate rows.
    /// </summary>
    private sealed class MetricsSnapshotDbDto
    {
        public int TotalPackages { get; init; }
        public int ActivePackages { get; init; }
        public int ExpiredPackages { get; init; }
        public int ExhaustedPackages { get; init; }
        public int ProtectedPackages { get; init; }
        public int EmptyPackages { get; init; }
        public int TotalItems { get; init; }
        public long TotalBytes { get; init; }
        public int PendingBlobDeletions { get; init; }
    }

    /// <summary>
    /// DTO for mapping content data from database.
    /// </summary>
    private sealed class ContentDbDto
    {
        public Guid Id { get; init; }
        public Guid PackageId { get; init; }
        public string ContentType { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public long Size { get; init; }
        public Guid? UserId { get; init; }
        public string? Meta { get; init; }
        public string Url { get; init; } = string.Empty;
    }
}
