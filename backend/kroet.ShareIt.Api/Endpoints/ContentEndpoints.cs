using System.Security.Claims;
using System.Text;
using kroet.Permission.AspNetCore;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.User;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace kroet.ShareIt.Api.Content;

/// <summary>
/// Extension methods for mapping content management endpoints.
/// </summary>
public static class ContentEndpoints
{
    /// <summary>
    /// Maps all content management endpoints to the route group.
    /// </summary>
    public static RouteGroupBuilder MapContentEndpoints(this RouteGroupBuilder group)
    {
        var packageGroup = group.MapGroup("/package");

        // Package endpoints
        packageGroup
            .MapPost("", CreatePackageAsync)
            .Accepts<CreatePackageRequest>("multipart/form-data")
            .RequirePermission("Create.Package")
            .Produces<ContentPackageResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .WithName("CreatePackage")
            .WithTags("Content", "Package");

        packageGroup
            .MapPost("/{packageId:guid}/upload", UploadFilesToPackageAsync)
            .Accepts<IFormFileCollection>("multipart/form-data")
            .RequirePermission("Upload.File")
            .Produces<ContentPackageResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
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
            .WithName("AddTextToPackage")
            .WithTags("Content", "Package");

        packageGroup
            .MapGet("/{packageId:guid}", GetPackageAsync)
            .RequirePermission("Read.Package")
            .Produces<ContentPackageDetailsResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status410Gone)
            .WithName("GetPackage")
            .WithTags("Content", "Package");

        // Content download endpoint (required for package file downloads)
        packageGroup
            .MapGet("/{packageId:guid}/content/{contentId:guid}/download", DownloadContentWithTokenAsync)
            .RequirePermission("Download.File")
            .Produces<FileStreamHttpResult>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
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
                    FileName = file.FileName,
                    MimeType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                };

                await using var stream = file.OpenReadStream();
                await storageHandler.StoreContentAsync(fileContent, stream, cancellationToken);
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
            }
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

        var now = timeProvider.GetUtcNow();
        // Upload each file and add to package
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
                FileName = file.FileName,
                MimeType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
            };

            await using var stream = file.OpenReadStream();
            await storageHandler.StoreContentAsync(fileContent, stream, cancellationToken);
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

        // Check if access limit has been reached
        if (package.IsAccessLimitReached)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package access limit reached",
                Detail = "The maximum number of downloads for this package has been reached",
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

        // Increment access count
        await storageHandler.IncrementPackageAccessCountAsync(packageId, cancellationToken);

        // Get all content items
        var items = new List<ContentItemInPackage>();

        foreach (var content in package.Contents)
        {
            if (content.ContentType == ContentType.Text)
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
                // Generate download token for file
                var token = await tokenService.GenerateTokenAsync(content.Id, userIdentity.Id, cancellationToken);
                var downloadUrl = linkGenerator.GetUriByName(
                    httpContext,
                    "DownloadContentWithToken",
                    new { packageId = package.Id, contentId = content.Id })!;

                items.Add(new ContentItemInPackage
                {
                    Id = content.Id,
                    Type = "file",
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
}