using System.Security.Claims;
using System.Text;
using kroet.Permission.AspNetCore;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.User;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Content;

/// <summary>
/// Extension methods for mapping content management endpoints.
/// </summary>
public static class ContentEndpoints
{
    /// <summary>
    /// Non-static logger category for this endpoint class (static classes
    /// cannot be used as <see cref="ILogger{T}"/> type arguments).
    /// </summary>
    private sealed class LogCategory { }

    /// <summary>
    /// Maps all content management endpoints to the route group.
    /// </summary>
    public static RouteGroupBuilder MapContentEndpoints(this RouteGroupBuilder group)
    {
        var packageGroup = group.MapGroup("/package");

        // Upload limits endpoint (placed before the parameterized routes)
        packageGroup
            .MapGet("/limits", GetUploadLimits)
            .RequirePermission("Read.Limits")
            .Produces<UploadLimitsResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .WithName("GetUploadLimits")
            .WithTags("Content", "Package");

        // Package endpoints
        packageGroup
            .MapPost("", CreatePackageAsync)
            .Accepts<CreatePackageRequest>("multipart/form-data")
            .RequirePermission("Create.Package")
            .DisableAntiforgery()
            .Produces<ContentPackageResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("CreatePackage")
            .WithTags("Content", "Package");

        packageGroup
            .MapPost("/{packageId:guid}/upload", UploadFilesToPackageAsync)
            .Accepts<IFormFileCollection>("multipart/form-data")
            .RequirePermission("Upload.File")
            .DisableAntiforgery()
            .Produces<ContentPackageResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("UploadFileToPackage")
            .WithTags("Content", "Package");

        packageGroup
            .MapPost("/{packageId:guid}/text", AddTextToPackageAsync)
            .Accepts<CreateTextRequest>("application/json")
            .RequirePermission("Upload.Text")
            .Produces<ContentPackageResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("AddTextToPackage")
            .WithTags("Content", "Package");

        packageGroup
            .MapGet("/{packageId:guid}", GetPackageAsync)
            .RequirePermission("Read.Package")
            .Produces<ContentPackageDetailsResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status410Gone)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("GetPackage")
            .WithTags("Content", "Package");

        // Owner preview: basic information only, no content, no tokens,
        // consumes no access. Authenticated owners only.
        packageGroup
            .MapGet("/{packageId:guid}/preview", GetPackagePreviewAsync)
            .RequirePermission("Preview.Package")
            .Produces<ContentPackagePreviewResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("GetPackagePreview")
            .WithTags("Content", "Package");

        // Content download endpoint (required for package file downloads)
        packageGroup
            .MapGet("/{packageId:guid}/content/{contentId:guid}/download", DownloadContentWithTokenAsync)
            .RequirePermission("Download.File")
            .Produces<FileStreamHttpResult>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("DownloadContentWithToken")
            .WithTags("Content");

        return packageGroup;
    }

    /// <summary>
    /// Creates a new content package with optional files and text content.
    /// </summary>
    private static async Task<Results<Created<ContentPackageResponse>, ProblemHttpResult>> CreatePackageAsync(
        [FromForm] CreatePackageRequest request,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] IStorageHandler storageHandler,
        [FromServices] TimeProvider timeProvider,
        [FromServices] LinkGenerator linkGenerator,
        [FromServices] IOptions<UploadSettings> uploadOptions,
        ILogger<LogCategory> logger,
        HttpContext httpContext,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (request.IsPrivate.HasValue && request.IsPrivate.Value && userIdentity.IsGuest)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Authentication required",
                Detail = "Only authenticated users can create private packages",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Validate upload sizes and lifetime before anything is stored
        var settings = uploadOptions.Value;
        var fileLimitBytes = FileLimitBytes(userIdentity, settings);
        if (request.Files is { Count: > 0 })
        {
            var oversized = request.Files.FirstOrDefault(file => file.Length > fileLimitBytes);
            if (oversized is not null)
            {
                return FileTooLargeProblem(oversized.FileName, fileLimitBytes);
            }
        }

        var lifetimeProblem = ValidateLifetime(userIdentity, settings, request.TimeToLiveHours);
        if (lifetimeProblem is not null)
        {
            return lifetimeProblem;
        }

        var downloadCountProblem = ValidateMaxDownloadCount(userIdentity, settings, request.MaxDownloadCount);
        if (downloadCountProblem is not null)
        {
            return downloadCountProblem;
        }

        var now = timeProvider.GetUtcNow();
        var package = new ContentPackage
        {
            Id = Guid.CreateVersion7(now),
            Title = request.Title,
            Description = request.Description,
            CreatedAt = now,
            UserId = userIdentity.Id,
            IsPrivate = request.IsPrivate,
            MaxDownloadCount = request.MaxDownloadCount,
            ExpiresAt = request.TimeToLiveHours.HasValue
                ? now.AddHours(request.TimeToLiveHours.Value)
                : null
        };

        // Wrap package + content storage so a failure while storing item N
        // also reverts items 1..N-1: the upload is atomic from the client's
        // point of view (either every item is stored, or none remains).
        var storedContentIds = new List<Guid>();
        try
        {
            await contentStorageHandler.StorePackageAsync(package, cancellationToken);

            // Process optional files
            if (request.Files is { Count: > 0 })
            {
                foreach (var file in request.Files)
                {
                    if (file.Length == 0)
                    {
                        continue;
                    }

                    var fileContent = new Api.Models.Content
                    {
                        Id = Guid.CreateVersion7(now),
                        PackageId = package.Id,
                        ContentType = ContentType.File,
                        CreatedAt = now,
                        Size = file.Length,
                        UserId = userIdentity.Id,
                        FileName = SanitizeFileName(file.FileName),
                        MimeType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    };

                    await using var stream = file.OpenReadStream();
                    await storageHandler.StoreContentAsync(fileContent, stream, cancellationToken);
                    storedContentIds.Add(fileContent.Id);
                }
            }

            // Process optional text content
            if (request.Texts is { Length: > 0 })
            {
                foreach (var text in request.Texts)
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var textBytes = Encoding.UTF8.GetBytes(text);
                    var textContent = new Api.Models.Content
                    {
                        Id = Guid.CreateVersion7(now),
                        PackageId = package.Id,
                        ContentType = ContentType.Text,
                        CreatedAt = now,
                        Size = textBytes.Length,
                        UserId = userIdentity.Id,
                        FileName = $"{Guid.NewGuid()}.txt",
                        MimeType = "text/plain",
                    };

                    await using var textStream = new MemoryStream(textBytes);
                    await storageHandler.StoreContentAsync(textContent, textStream, cancellationToken);
                    storedContentIds.Add(textContent.Id);
                }
            }
        }
        catch
        {
            await RollbackStoredContentAsync(
                package.Id,
                storedContentIds,
                contentStorageHandler,
                storageHandler,
                logger,
                deletePackage: true,
                cancellationToken);
            throw;
        }

        // Reload package with content to get accurate item count
        var updatedPackage = await contentStorageHandler.GetPackageAsync(package.Id, true, cancellationToken);

        var packageUrl = linkGenerator.GetUriByName(httpContext, "GetPackage", new { packageId = package.Id })!;

        return TypedResults.Created(packageUrl, ContentPackageResponse.FromPackage(updatedPackage ?? package));
    }

    /// <summary>
    /// Uploads multiple files to an existing package.
    /// </summary>
    private static async Task<Results<Ok<ContentPackageResponse>, ProblemHttpResult>> UploadFilesToPackageAsync(
        [FromRoute] Guid packageId,
        [FromForm] IFormFileCollection files,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] IStorageHandler storageHandler,
        [FromServices] TimeProvider timeProvider,
        [FromServices] IOptions<UploadSettings> uploadOptions,
        ILogger<LogCategory> logger,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "No files provided", Detail = "At least one file must be uploaded", Status = StatusCodes.Status400BadRequest
            });
        }

        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Verify package exists
        var package = await contentStorageHandler.GetPackageAsync(packageId, false, cancellationToken);
        if (package is null)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package not found", Detail = $"Package with ID {packageId} was not found", Status = StatusCodes.Status404NotFound
            });
        }

        if (package.UserId != userIdentity.Id)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Forbidden", Detail = "Only the owner of the package can add files to it", Status = StatusCodes.Status403Forbidden
            });
        }

        // Validate upload sizes before anything is stored
        var settings = uploadOptions.Value;
        var fileLimitBytes = FileLimitBytes(userIdentity, settings);
        var oversized = files.FirstOrDefault(file => file.Length > fileLimitBytes);
        if (oversized is not null)
        {
            return FileTooLargeProblem(oversized.FileName, fileLimitBytes);
        }

        var now = timeProvider.GetUtcNow();

        // Wrap content storage so a failure while storing file N also reverts
        // files 1..N-1 added by this request; previously stored package content
        // (and the package itself) is intentionally left untouched.
        var storedContentIds = new List<Guid>();
        try
        {
            foreach (var file in files)
            {
                if (file.Length == 0)
                {
                    continue;
                }

                var fileContent = new Api.Models.Content
                {
                    Id = Guid.CreateVersion7(now),
                    PackageId = packageId,
                    ContentType = ContentType.File,
                    CreatedAt = now,
                    Size = file.Length,
                    UserId = userIdentity.Id,
                    FileName = SanitizeFileName(file.FileName),
                    MimeType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                };

                await using var stream = file.OpenReadStream();
                await storageHandler.StoreContentAsync(fileContent, stream, cancellationToken);
                storedContentIds.Add(fileContent.Id);
            }
        }
        catch
        {
            await RollbackStoredContentAsync(
                packageId,
                storedContentIds,
                contentStorageHandler,
                storageHandler,
                logger,
                deletePackage: false,
                cancellationToken);
            throw;
        }

        // Get updated package
        package = await contentStorageHandler.GetPackageAsync(packageId, true, cancellationToken);

        return TypedResults.Ok(ContentPackageResponse.FromPackage(package!));
    }

    /// <summary>
    /// Adds text content to an existing package.
    /// </summary>
    private static async Task<Results<Ok<ContentPackageResponse>, ProblemHttpResult>> AddTextToPackageAsync(
        [FromRoute] Guid packageId,
        [FromBody] CreateTextRequest request,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] IStorageHandler storageHandler,
        [FromServices] TimeProvider timeProvider,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Invalid text", Detail = "Text content cannot be empty", Status = StatusCodes.Status400BadRequest
            });
        }

        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Verify package exists
        var package = await contentStorageHandler.GetPackageAsync(packageId, false, cancellationToken);
        if (package is null)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package not found", Detail = $"Package with ID {packageId} was not found", Status = StatusCodes.Status404NotFound
            });
        }

        if (package.UserId != userIdentity.Id)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Forbidden", Detail = "Only the owner of the package can add text content to it", Status = StatusCodes.Status403Forbidden
            });
        }

        var now = timeProvider.GetUtcNow();
        var textBytes = Encoding.UTF8.GetBytes(request.Text);
        var textContent = new Api.Models.Content
        {
            Id = Guid.CreateVersion7(now),
            PackageId = packageId,
            ContentType = ContentType.Text,
            CreatedAt = now,
            Size = textBytes.Length,
            UserId = userIdentity.Id,
            FileName = $"{Guid.NewGuid()}.txt",
            MimeType = "text/plain",
        };

        await using var textStream = new MemoryStream(textBytes);
        await storageHandler.StoreContentAsync(textContent, textStream, cancellationToken);

        // Get updated package
        package = await contentStorageHandler.GetPackageAsync(packageId, true, cancellationToken);

        return TypedResults.Ok(ContentPackageResponse.FromPackage(package!));
    }

    /// <summary>
    /// Gets a package with all its content items.
    /// Generates download tokens for files.
    /// </summary>
    private static async Task<Results<Ok<ContentPackageDetailsResponse>, ProblemHttpResult>> GetPackageAsync(
        [FromRoute] Guid packageId,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler storageHandler,
        [FromServices] IBlobStorageHandler blobStorageHandler,
        [FromServices] IDownloadTokenService tokenService,
        [FromServices] IOptions<DownloadSettings> downloadOptions,
        [FromServices] LinkGenerator linkGenerator,
        [FromServices] TimeProvider timeProvider,
        HttpContext httpContext,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var package = await storageHandler.GetPackageAsync(packageId, true, cancellationToken);
        if (package is null || package.IsPrivate.HasValue && package.IsPrivate.Value && (userIdentity.IsGuest || package.UserId != userIdentity.Id))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package not found", Detail = $"Package with ID {packageId} was not found", Status = StatusCodes.Status404NotFound
            });
        }

        var now = timeProvider.GetUtcNow();

        // Check if package has expired
        if (package.IsExpired(now))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package expired", Detail = "This package has expired and is no longer available", Status = StatusCodes.Status410Gone
            });
        }

        // Purely accessing the link consumes one access, claimed atomically:
        // once the package's access limit is reached, any further view —
        // another person, a second tab, or a refresh — is rejected here.
        var claim = await storageHandler.IncrementPackageAccessCountAsync(packageId, cancellationToken);
        if (!claim.Success)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package access limit reached",
                Detail = "The maximum number of accesses for this package has been reached",
                Status = StatusCodes.Status410Gone
            });
        }

        if (package.Contents == null || package.Contents.Count == 0)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package is empty", Detail = "This package contains no content", Status = StatusCodes.Status410Gone
            });
        }

        // Get all content items
        var items = new List<ContentItemInPackage>();

        // Only read text blobs fully into memory up to the configured inline
        // limit; larger text items are served via a download token like files.
        var inlineTextLimitBytes = downloadOptions.Value.InlineTextLimitKb * 1024;

        foreach (var content in package.Contents)
        {
            if (content.ContentType == ContentType.Text && content.Size <= inlineTextLimitBytes)
            {
                var dataStream = await blobStorageHandler.DownloadAsync(content.Url, cancellationToken);
                using var reader = new StreamReader(dataStream, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(cancellationToken);

                items.Add(new ContentItemInPackage
                {
                    Id = content.Id,
                    Type = "text",
                    MimeType = content.MimeType,
                    Size = content.Size,
                    Text = text
                });
            }
            else
            {
                // Generate download token for files and oversized text items
                var token = await tokenService.GenerateTokenAsync(content.Id, userIdentity.Id, cancellationToken);
                var downloadUrl = linkGenerator.GetUriByName(
                    httpContext,
                    "DownloadContentWithToken",
                    new { packageId = package.Id, contentId = content.Id })!;

                items.Add(new ContentItemInPackage
                {
                    Id = content.Id,
                    Type = content.ContentType == ContentType.Text ? "text" : "file",
                    FileName = content.FileName,
                    MimeType = content.MimeType,
                    Size = content.Size,
                    DownloadToken = token.Token,
                    DownloadUrl = downloadUrl,
                    TokenExpiresAt = token.ExpiresAt
                });
            }
        }

        var response = new ContentPackageDetailsResponse { Package = ContentPackageResponse.FromPackage(package), Items = items };

        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Returns basic package information for the authenticated owner only:
    /// no content items, no download tokens, no inline text, and it never
    /// consumes an access.
    /// </summary>
    private static async Task<Results<Ok<ContentPackagePreviewResponse>, ProblemHttpResult>> GetPackagePreviewAsync(
        [FromRoute] Guid packageId,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler storageHandler,
        [FromServices] TimeProvider timeProvider,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Guests are consumers and must never be able to preview packages.
        if (userIdentity.IsGuest)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Authentication required",
                Detail = "Only authenticated users can preview their packages",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var package = await storageHandler.GetPackageAsync(packageId, true, cancellationToken);

        // Owner-only; a non-owner gets a 404 so other users' packages are not revealed.
        if (package is null || package.UserId != userIdentity.Id)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package not found", Detail = $"Package with ID {packageId} was not found", Status = StatusCodes.Status404NotFound
            });
        }

        return TypedResults.Ok(new ContentPackagePreviewResponse
        {
            Id = package.Id,
            Title = package.Title,
            Description = package.Description,
            CreatedAt = package.CreatedAt,
            ExpiresAt = package.ExpiresAt,
            ItemCount = package.Contents?.Count ?? 0,
            MaxDownloadCount = package.MaxDownloadCount,
            CurrentDownloadCount = package.CurrentDownloadCount,
            RemainingDownloads = package.RemainingDownloads,
            IsExpired = package.IsExpired(timeProvider.GetUtcNow())
        });
    }

    /// <summary>
    /// Downloads content data using a temporary token.
    /// </summary>
    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> DownloadContentWithTokenAsync(
        [FromRoute] Guid packageId,
        [FromRoute] Guid contentId,
        [FromHeader(Name = "X-File-Token")] string token,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler storageHandler,
        [FromServices] IBlobStorageHandler blobStorageHandler,
        [FromServices] IDownloadTokenService tokenService,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Missing download token",
                Detail = "A download token must be provided in the X-File-Token header",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Validate the token
        var validationResult = await tokenService.ValidateAndConsumeTokenAsync(token, userIdentity.Id, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Invalid download token",
                Detail = validationResult.ErrorMessage ?? "The download token is invalid or has expired",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Verify the token was issued for this content
        if (validationResult.ContentId != contentId)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Token mismatch", Detail = "The download token is not valid for this content", Status = StatusCodes.Status400BadRequest
            });
        }

        // Verify the content is part of the specified package
        var package = await storageHandler.GetPackageAsync(packageId, true, cancellationToken);
        if (package?.Contents is null || package.Contents.All(c => c.Id != contentId))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Content not in package",
                Detail = "The requested content is not part of the specified package",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            // Get metadata first to determine content type and filename
            var content = package.Contents.FirstOrDefault(c => c.Id == contentId);

            if (content == null)
            {
                return TypedResults.Problem(new ProblemDetails
                {
                    Title = "Content not found", Detail = $"Content with ID {contentId} was not found", Status = StatusCodes.Status404NotFound
                });
            }

            // No counter interaction here: the access was already claimed when
            // the package link was opened. Downloads are enforced by the
            // single-use token only.

            // Retrieve the data stream
            var stream = await blobStorageHandler.DownloadAsync(content.Url, cancellationToken);

            return TypedResults.Stream(
                stream,
                content.MimeType,
                content.FileName,
                content.CreatedAt,
                enableRangeProcessing: true);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Content not found", Detail = $"Content with ID {contentId} was not found", Status = StatusCodes.Status404NotFound
            });
        }
    }

    /// <summary>
    /// Returns the upload size limits for anonymous and authenticated users.
    /// </summary>
    private static Ok<UploadLimitsResponse> GetUploadLimits(
        [FromServices] IOptions<UploadSettings> uploadOptions)
    {
        var settings = uploadOptions.Value;

        return TypedResults.Ok(new UploadLimitsResponse
        {
            AnonymousFileLimitBytes = settings.Anonymous.FileLimitMb * 1024L * 1024L,
            AuthenticatedFileLimitBytes = settings.Authenticated.FileLimitMb * 1024L * 1024L,
            AnonymousMaxLifetimeHours = settings.Anonymous.MaxLifetimeHours,
            AuthenticatedMaxLifetimeHours = settings.Authenticated.MaxLifetimeHours,
            AnonymousMaxDownloadCount = settings.Anonymous.MaxDownloadCount,
            AuthenticatedMaxDownloadCount = settings.Authenticated.MaxDownloadCount
        });
    }

    /// <summary>
    /// Resolves the per-file upload limit for the given identity.
    /// </summary>
    private static long FileLimitBytes(UserIdentity userIdentity, UploadSettings settings)
    {
        var identityLimits = userIdentity.IsGuest ? settings.Anonymous : settings.Authenticated;
        return identityLimits.FileLimitMb * 1024L * 1024L;
    }

    /// <summary>
    /// Enforces the per-identity package lifetime limits: anonymous users are
    /// capped at a short lifetime (and must set one at all), authenticated
    /// users may go up to the configured maximum or omit it (no expiration).
    /// </summary>
    private static ProblemHttpResult? ValidateLifetime(UserIdentity userIdentity, UploadSettings settings, int? timeToLiveHours)
    {
        if (timeToLiveHours is { } hours)
        {
            if (hours <= 0)
            {
                return BadRequestProblem(
                    "Invalid lifetime",
                    "TimeToLiveHours must be a positive number of hours.");
            }

            var identityLimits = userIdentity.IsGuest ? settings.Anonymous : settings.Authenticated;
            if (hours > identityLimits.MaxLifetimeHours)
            {
                return BadRequestProblem(
                    "Lifetime too long",
                    $"Packages for this identity may live for at most {identityLimits.MaxLifetimeHours} hours.");
            }

            return null;
        }

        if (userIdentity.IsGuest)
        {
            return BadRequestProblem(
                "Lifetime required",
                $"Anonymous packages must set TimeToLiveHours of at most {settings.Anonymous.MaxLifetimeHours} hours.");
        }

        return null;
    }

    private static ProblemHttpResult? ValidateMaxDownloadCount(
        UserIdentity userIdentity, UploadSettings settings, int? maxDownloadCount)
    {
        if (maxDownloadCount is { } count)
        {
            if (count <= 0)
            {
                return BadRequestProblem(
                    "Invalid download limit",
                    "MaxDownloadCount must be a positive number.");
            }

            var identityLimits = userIdentity.IsGuest ? settings.Anonymous : settings.Authenticated;
            if (count > identityLimits.MaxDownloadCount)
            {
                return BadRequestProblem(
                    "Download limit too high",
                    $"Packages for this identity allow at most {identityLimits.MaxDownloadCount} downloads.");
            }

            return null;
        }

        if (userIdentity.IsGuest)
        {
            return BadRequestProblem(
                "Download limit required",
                $"Anonymous packages must set MaxDownloadCount of at most {settings.Anonymous.MaxDownloadCount}.");
        }

        return null;
    }

    private static ProblemHttpResult BadRequestProblem(string title, string detail)
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status400BadRequest
        });
    }

    /// <summary>
    /// Best-effort cleanup of a partially stored upload: deletes every content
    /// item that was fully stored during this request (blob + metadata) and,
    /// for package creation, the package metadata row itself. Mirrors the
    /// single-item cleanup in <see cref="StorageHandler.StoreContentAsync"/>
    /// at the multi-item level.
    /// </summary>
    private static async Task RollbackStoredContentAsync(
        Guid packageId,
        IReadOnlyList<Guid> storedContentIds,
        IContentStorageHandler contentStorageHandler,
        IStorageHandler storageHandler,
        ILogger<LogCategory> logger,
        bool deletePackage,
        CancellationToken cancellationToken)
    {
        foreach (var contentId in storedContentIds)
        {
            try
            {
                if (await storageHandler.DeleteContentAsync(contentId, cancellationToken))
                {
                    logger.LogDebug("Rolled back content {ContentId} after failed upload", contentId);
                }
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(cleanupEx, "Failed to roll back content {ContentId} after failed upload", contentId);
            }
        }

        if (deletePackage)
        {
            try
            {
                if (await contentStorageHandler.DeletePackageAsync(packageId, cancellationToken))
                {
                    logger.LogInformation("Rolled back package {PackageId} after failed upload", packageId);
                }
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(cleanupEx, "Failed to roll back package {PackageId} after failed upload", packageId);
            }
        }
    }

    private static ProblemHttpResult FileTooLargeProblem(string? fileName, long fileLimitBytes)
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Title = "File too large",
            Detail = $"File '{fileName}' exceeds the upload limit of {fileLimitBytes / (1024d * 1024d):0.#} MB.",
            Status = StatusCodes.Status413PayloadTooLarge
        });
    }

    /// <summary>
    /// Strips any path information, control characters and excessive length from a
    /// client-supplied file name before it is persisted or used in response headers.
    /// </summary>
    private static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        var sanitized = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (string.IsNullOrEmpty(sanitized))
        {
            return "file";
        }

        const int maxFileNameLength = 200;
        return sanitized.Length <= maxFileNameLength ? sanitized : sanitized[^maxFileNameLength..];
    }
}