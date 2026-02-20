using Dapper;
using kroet.ShareIt.Api.Content.Configuration;
using kroet.ShareIt.Api.Content.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace kroet.ShareIt.Api.Content.Storage.Package;

/// <summary>
/// PostgreSQL implementation of content package storage using Dapper.
/// </summary>
public class SqlContentStorageHandler : IContentStorageHandler
{
    private readonly PostgreSqlContentStorageSettings _settings;
    private readonly ILogger<SqlContentStorageHandler> _logger;

    public SqlContentStorageHandler(
        IOptions<PostgreSqlContentStorageSettings> settings,
        ILogger<SqlContentStorageHandler> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StorePackageAsync(ContentPackage package, CancellationToken cancellationToken = default)
    {
        const string insertPackageSql = """
                                        INSERT INTO content_packages (
                                            id, title, description, created_at, user_id, 
                                            max_download_count, current_download_count, expires_at
                                        ) VALUES (
                                            @Id, @Title, @Description, @CreatedAt, @UserId,
                                            @MaxDownloadCount, 0, @ExpiresAt
                                        );
                                        """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            insertPackageSql,
            new
            {
                package.Id,
                package.Title,
                package.Description,
                package.CreatedAt,
                package.UserId,
                package.MaxDownloadCount,
                package.ExpiresAt
            });

        _logger.LogInformation(
            "Stored content package {PackageId}", 
            package.Id);
    }

    /// <inheritdoc />
    public async Task StoreContentAsync(ExternalContent content, CancellationToken cancellationToken = default)
    {
        const string insertSql = """
                                 INSERT INTO contents (
                                     id, package_id, content_type, created_at, size, user_id, 
                                     mime_type, file_name, url
                                 ) VALUES (
                                     @Id, @PackageId, @ContentType, @CreatedAt, @Size, @UserId,
                                     @MimeType, @FileName, @Url
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
                content.MimeType,
                content.FileName,
                content.Url
            });

        _logger.LogInformation(
            "Stored content {ContentId} in package {PackageId}",
            content.Id,
            content.PackageId);
    }

    /// <inheritdoc />
    public async Task StoreContentsAsync(IEnumerable<ExternalContent> contents, CancellationToken cancellationToken = default)
    {
        var contentList = contents.ToList();
        if (contentList.Count == 0)
        {
            return;
        }

        const string insertSql = """
                                 INSERT INTO contents (
                                     id, package_id, content_type, created_at, size, user_id, 
                                     mime_type, file_name, url
                                 ) VALUES (
                                     @Id, @PackageId, @ContentType, @CreatedAt, @Size, @UserId,
                                     @MimeType, @FileName, @Url
                                 );
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var parameters = contentList.Select(content => new
        {
            content.Id,
            content.PackageId,
            ContentType = content.ContentType.ToString(),
            content.CreatedAt,
            content.Size,
            content.UserId,
            content.MimeType,
            content.FileName,
            content.Url
        });

        await connection.ExecuteAsync(insertSql, parameters);

        _logger.LogInformation("Stored {Count} contents", contentList.Count);
    }

    /// <inheritdoc />
    public async Task<ContentPackage?> GetPackageAsync(Guid packageId, bool includeContent = true, CancellationToken cancellationToken = default)
    {
        const string getPackageSql = """
                                     SELECT 
                                         id, title, description, created_at, user_id,
                                         max_download_count, current_download_count, expires_at
                                     FROM content_packages
                                     WHERE id = @PackageId
                                     """;
        
        const string getContentSql = """
                                        SELECT 
                                            id, package_id, content_type, created_at, size, user_id,
                                            mime_type, file_name, url
                                        FROM contents
                                        WHERE package_id = @PackageId
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
            package.Contents = contentDtoList.Select(MapToExternalContent).ToList();
        }

        return package;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContentPackage>> GetPackagesAsync(IEnumerable<Guid> packageIds, bool includeContent = true, CancellationToken cancellationToken = default)
    {
        var packageIdList = packageIds.ToList();
        if (packageIdList.Count == 0)
        {
            return [];
        }

        const string getPackagesSql = """
                                      SELECT 
                                          id, title, description, created_at, user_id,
                                          max_download_count, current_download_count, expires_at
                                      FROM content_packages
                                      WHERE id = ANY(@PackageIds)
                                      """;

        const string getContentSql = """
                                        SELECT 
                                            id, package_id, content_type, created_at, size, user_id,
                                            mime_type, file_name, url
                                        FROM contents
                                        WHERE package_id = ANY(@PackageIds)
                                        ORDER BY package_id, created_at
                                        """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Get all packages
        var packageDtoList = await connection.QueryAsync<PackageDbDto>(
            getPackagesSql,
            new { PackageIds = packageIdList.ToArray() });

        var packages = packageDtoList.Select(MapToContentPackage).ToList();

        // If includeContent is true, fetch all content objects
        if (includeContent)
        {
            var contentDtoList = await connection.QueryAsync<ContentDbDto>(
                getContentSql,
                new { PackageIds = packageIdList.ToArray() });

            var contentIdsByPackage = contentDtoList
                .GroupBy(x => x.PackageId)
                .ToDictionary(g => g.Key, g => g);
            
            foreach (var package in packages)
            {
                if (contentIdsByPackage.TryGetValue(package.Id, out var contents))
                {
                    package.Contents = contents.Select(MapToExternalContent).ToList();
                }
            }
        }

        return packages;
    }

    /// <inheritdoc />
    public async Task<ExternalContent?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        const string selectSql = """
                                 SELECT 
                                     id, package_id, content_type, created_at, size, user_id,
                                     mime_type, file_name, url
                                 FROM contents
                                 WHERE id = @ContentId
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var dto = await connection.QuerySingleOrDefaultAsync<ContentDbDto>(
            selectSql,
            new { ContentId = contentId });

        if (dto == null)
            return null;

        return MapToExternalContent(dto);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalContent>> GetContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default)
    {
        var contentIdList = contentIds.ToList();
        if (contentIdList.Count == 0)
        {
            return [];
        }

        const string selectSql = """
                                 SELECT 
                                     id, package_id, content_type, created_at, size, user_id,
                                     mime_type, file_name, url
                                 FROM contents
                                 WHERE id = ANY(@ContentIds)
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var dtoList = await connection.QueryAsync<ContentDbDto>(
            selectSql,
            new { ContentIds = contentIdList.ToArray() });

        return dtoList.Select(MapToExternalContent).ToList();
    }

    /// <inheritdoc />
    public async Task<DownloadIncrementResult> IncrementPackageAccessCountAsync(
        Guid packageId, 
        CancellationToken cancellationToken = default)
    {
        // Using UPDATE...RETURNING for atomic increment with a single query
        const string incrementSql = """
                                    UPDATE content_packages
                                        SET current_download_count = current_download_count + 1
                                    WHERE id = @PackageId
                                    RETURNING current_download_count, max_download_count
                                    """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var result = await connection.QuerySingleOrDefaultAsync<DownloadCountDbDto>(
            incrementSql,
            new { PackageId = packageId });

        if (result == null)
        {
            _logger.LogWarning("Failed to increment access count - package {PackageId} not found", packageId);
            return new DownloadIncrementResult(false, 0, null, false);
        }

        var limitReached = result.MaxDownloadCount.HasValue && 
                          result.CurrentDownloadCount >= result.MaxDownloadCount.Value;

        _logger.LogDebug(
            "Incremented access count for package {PackageId}: {Current}/{Max}",
            packageId, 
            result.CurrentDownloadCount, 
            result.MaxDownloadCount?.ToString() ?? "unlimited");

        return new DownloadIncrementResult(
            true,
            result.CurrentDownloadCount,
            result.MaxDownloadCount,
            limitReached);
    }

    /// <inheritdoc />
    public async Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        const string deletePackageSql = "DELETE FROM content_packages WHERE id = @PackageId";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            deletePackageSql,
            new { PackageId = packageId });

        var deleted = affected > 0;
        if (deleted)
        {
            _logger.LogInformation("Deleted content package {PackageId}", packageId);
        }

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

        const string deletePackagesSql = """
                                         DELETE FROM content_packages 
                                         WHERE id = ANY(@PackageIds)
                                         """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rowsAffected = await connection.ExecuteAsync(
            deletePackagesSql,
            new { PackageIds = packageIdList.ToArray() });
        
        _logger.LogInformation("Deleted {Count} packages", rowsAffected);

        return rowsAffected;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        const string deleteSql = """
                                 DELETE FROM contents
                                 WHERE id = @ContentId
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rowsAffected = await connection.ExecuteAsync(
            deleteSql,
            new { ContentId = contentId });

        var deleted = rowsAffected > 0;
        if (deleted)
        {
            _logger.LogInformation("Deleted content {ContentId}", contentId);
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<int> DeleteContentsAsync(IEnumerable<Guid> contentIds, CancellationToken cancellationToken = default)
    {
        var contentIdList = contentIds.ToList();
        if (contentIdList.Count == 0)
        {
            return 0;
        }

        const string deleteSql = """
                                 DELETE FROM contents
                                 WHERE id = ANY(@ContentIds)
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rowsAffected = await connection.ExecuteAsync(
            deleteSql,
            new { ContentIds = contentIdList.ToArray() });
        
        _logger.LogInformation("Deleted {Count} contents", rowsAffected);

        return rowsAffected;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        const string existsSql = """
                                 SELECT EXISTS(
                                     SELECT 1 FROM content_packages WHERE id = @PackageId
                                 )
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<bool>(
            existsSql,
            new { PackageId = packageId });

        return exists;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsContentAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        const string existsSql = """
                                 SELECT EXISTS(
                                     SELECT 1 FROM contents WHERE id = @ContentId
                                 )
                                 """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<bool>(
            existsSql,
            new { ContentId = contentId });

        return exists;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default)
    {
        const string getExpiredPackagesSql = """
                                             SELECT id
                                             FROM content_packages
                                             WHERE 
                                                 (max_download_count IS NOT NULL AND current_download_count >= max_download_count)
                                                 OR (expires_at IS NOT NULL AND expires_at < @Now)
                                             """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var packageIds = await connection.QueryAsync<Guid>(
            getExpiredPackagesSql,
            new { Now = DateTimeOffset.UtcNow });

        var result = packageIds.ToList();
        
        if (result.Count > 0)
        {
            _logger.LogInformation("Found {Count} packages to delete", result.Count);
        }

        return result;
    }
    
    private NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(_settings.ConnectionString);
    }

    /// <summary>
    /// Maps a DTO to ExternalContent domain model.
    /// </summary>
    private static ExternalContent MapToExternalContent(ContentDbDto dbDto)
    {
        return new ExternalContent
        {
            Id = dbDto.Id,
            PackageId = dbDto.PackageId,
            ContentType = Enum.Parse<ContentType>(dbDto.ContentType),
            CreatedAt = dbDto.CreatedAt,
            Size = dbDto.Size,
            UserId = dbDto.UserId,
            MimeType = dbDto.MimeType,
            FileName = dbDto.FileName,
            Url = dbDto.Url
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
            Title = dbDto.Title,
            Description = dbDto.Description,
            CreatedAt = dbDto.CreatedAt,
            UserId = dbDto.UserId,
            MaxDownloadCount = dbDto.MaxDownloadCount,
            CurrentDownloadCount = dbDto.CurrentDownloadCount,
            ExpiresAt = dbDto.ExpiresAt
        };
    }

    /// <summary>
    /// DTO for mapping package data from database.
    /// </summary>
    private class PackageDbDto
    {
        public Guid Id { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public Guid? UserId { get; init; }
        public int? MaxDownloadCount { get; init; }
        public int CurrentDownloadCount { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
    }

    /// <summary>
    /// DTO for mapping download count results.
    /// </summary>
    private class DownloadCountDbDto
    {
        public int CurrentDownloadCount { get; init; }
        public int? MaxDownloadCount { get; init; }
    }

    /// <summary>
    /// DTO for mapping content data from database.
    /// </summary>
    private class ContentDbDto
    {
        public Guid Id { get; init; }
        public Guid PackageId { get; init; }
        public string ContentType { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public long Size { get; init; }
        public Guid? UserId { get; init; }
        public string MimeType { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
    }
}

