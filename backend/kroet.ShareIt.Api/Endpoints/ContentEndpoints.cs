using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Claims;
using kroet.Permission.AspNetCore;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.User;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

        // Package creation: metadata only. Content is added one item per
        // request through /upload, which is the single content write path.
        packageGroup
            .MapPost("", CreatePackageAsync)
            .Accepts<CreatePackageRequest>("application/json")
            .RequirePermission("Create.Package")
            .Produces<ContentPackageResponse>(StatusCodes.Status201Created)
            .Produces<ContentPackageResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("CreatePackage")
            .WithTags("Content", "Package");

        packageGroup
            .MapPost("/{packageId:guid}/upload", UploadContentToPackageAsync)
            .Accepts<UploadContentRequest>("multipart/form-data")
            .RequirePermission("Upload.File")
            .DisableAntiforgery()
            .Produces<ContentPackageResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("UploadFileToPackage")
            .WithTags("Content", "Package");

        packageGroup
            .MapGet("/{packageId:guid}", GetPackageAsync)
            .RequirePermission("Read.Package")
            .RequireRateLimiting(RateLimitPolicies.PackageRead)
            .Produces<ContentPackageDetailsResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status410Gone)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("GetPackage")
            .WithTags("Content", "Package");

        // Content download endpoint (required for items above the inline limit)
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
    /// Creates a new (empty) content package. The client supplies the id
    /// because the salt and verifier derive from it; repeating the request
    /// with the same id is idempotent for its owner.
    /// </summary>
    private static async Task<Results<Created<ContentPackageResponse>, Ok<ContentPackageResponse>, ProblemHttpResult>>
        CreatePackageAsync(
            [FromBody] CreatePackageRequest request,
            [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
            [FromServices] IContentStorageHandler contentStorageHandler,
            [FromServices] TimeProvider timeProvider,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] IOptions<UploadSettings> uploadOptions,
            [FromServices] ShareItMetrics metrics,
            ClaimsPrincipal claimsPrincipal,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.create");

        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        activity?.SetTag("shareit.identity.type", userIdentity.IsGuest ? "guest" : "user");

        if (request.Id is not { } packageId || packageId == Guid.Empty)
        {
            return BadRequestProblem("Package id required", "The client must supply a non-empty package id.");
        }

        if (request.UnlockVerifier is not null && !UnlockProof.TryValidateVerifier(request.UnlockVerifier, out _))
        {
            return BadRequestProblem(
                "Invalid unlock verifier",
                $"UnlockVerifier must be base64url of exactly {UnlockProof.VerifierLength} bytes.");
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

        var settings = uploadOptions.Value;
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

        // Idempotent retry: the same owner repeating the create call gets the
        // existing package back. Another identity must not probe/hijack the id.
        var existing = await contentStorageHandler.GetPackageShellAsync(packageId, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != userIdentity.Id)
            {
                return TypedResults.Problem(new ProblemDetails
                {
                    Title = "Package id already in use",
                    Detail = "A package with this id already exists.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            var existingWithContent = await contentStorageHandler.GetPackageAsync(packageId, true, cancellationToken);
            activity?.SetTag("shareit.package.result", "existing");
            return TypedResults.Ok(ContentPackageResponse.FromPackage(existingWithContent ?? existing));
        }

        var now = timeProvider.GetUtcNow();
        var package = new ContentPackage
        {
            Id = packageId,
            CreatedAt = now,
            UserId = userIdentity.Id,
            IsPrivate = request.IsPrivate,
            MaxDownloadCount = request.MaxDownloadCount,
            ExpiresAt = request.TimeToLiveHours.HasValue
                ? now.AddHours(request.TimeToLiveHours.Value)
                : null,
            UnlockVerifier = request.UnlockVerifier
        };

        await contentStorageHandler.StorePackageAsync(package, cancellationToken);
        metrics.PackageCreated();
        activity?.SetTag("shareit.package.result", "created");
        activity?.SetTag("shareit.package.private", request.IsPrivate ?? false);

        var packageUrl = linkGenerator.GetUriByName(httpContext, "GetPackage", new { packageId = package.Id })!;

        return TypedResults.Created(packageUrl, ContentPackageResponse.FromPackage(package));
    }

    /// <summary>
    /// Uploads one item — text bytes or file bytes — to an existing package.
    /// The bytes and the opaque metadata are stored verbatim; the server never
    /// parses the metadata.
    /// </summary>
    private static async Task<Results<Ok<ContentPackageResponse>, ProblemHttpResult>> UploadContentToPackageAsync(
        [FromRoute] Guid packageId,
        [FromForm] UploadContentRequest request,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] IStorageHandler storageHandler,
        [FromServices] TimeProvider timeProvider,
        [FromServices] IOptions<UploadSettings> uploadOptions,
        [FromServices] ShareItMetrics metrics,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.upload");

        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        activity?.SetTag("shareit.identity.type", userIdentity.IsGuest ? "guest" : "user");

        var package = await contentStorageHandler.GetPackageShellAsync(packageId, cancellationToken);
        if (package is null)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package not found",
                Detail = $"Package with ID {packageId} was not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (package.UserId != userIdentity.Id)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Only the owner of the package can add content to it",
                Status = StatusCodes.Status403Forbidden
            });
        }

        if (request.Id is not { } contentId || contentId == Guid.Empty)
        {
            return BadRequestProblem("Content id required", "The client must supply a non-empty content id.");
        }

        if (!TryParseKind(request.Kind, out var contentType))
        {
            return BadRequestProblem("Invalid kind", "Kind must be either 'text' or 'file'.");
        }

        if (!Base64UrlText.TryDecode(request.Meta, out var metaBytes))
        {
            return BadRequestProblem("Invalid meta", "Meta must be base64url text.");
        }

        if (metaBytes.Length > UnlockProof.MaxMetaBytes)
        {
            return BadRequestProblem(
                "Meta too large",
                $"Meta must decode to at most {UnlockProof.MaxMetaBytes} bytes.");
        }

        if (request.File is null || request.File.Length == 0)
        {
            return BadRequestProblem("Empty content", "The uploaded content must not be empty.");
        }

        activity?.SetTag("shareit.content.kind", KindOf(contentType));
        activity?.SetTag("shareit.content.size_bytes", request.File.Length);

        var settings = uploadOptions.Value;
        var fileLimitBytes = FileLimitBytes(userIdentity, settings);
        if (request.File.Length > fileLimitBytes)
        {
            return FileTooLargeProblem(contentId, fileLimitBytes);
        }

        // Idempotent retry: a repeated upload with the same item id must not
        // create a duplicate row or fail on the primary key.
        var existing = await contentStorageHandler.GetContentAsync(contentId, cancellationToken);
        if (existing is not null)
        {
            if (existing.PackageId != packageId)
            {
                return TypedResults.Problem(new ProblemDetails
                {
                    Title = "Content id already in use",
                    Detail = "Content with this id already exists in another package.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            var existingPackage = await contentStorageHandler.GetPackageAsync(packageId, true, cancellationToken);
            activity?.SetTag("shareit.package.result", "existing");
            return TypedResults.Ok(ContentPackageResponse.FromPackage(existingPackage ?? package));
        }

        var now = timeProvider.GetUtcNow();
        var content = new Api.Models.Content
        {
            Id = contentId,
            PackageId = packageId,
            ContentType = contentType,
            CreatedAt = now,
            Size = request.File.Length,
            UserId = userIdentity.Id,
            Meta = request.Meta
        };

        await using var stream = request.File.OpenReadStream();
        await storageHandler.StoreContentAsync(content, stream, cancellationToken);
        metrics.ItemUploaded(contentType, request.File.Length);
        activity?.SetTag("shareit.package.result", "uploaded");

        var updatedPackage = await contentStorageHandler.GetPackageAsync(packageId, true, cancellationToken);

        return TypedResults.Ok(ContentPackageResponse.FromPackage(updatedPackage ?? package));
    }

    /// <summary>
    /// Gets a package with all its content items and issues download tokens for
    /// items above the inline payload limit. A protected package requires a
    /// valid unlock proof before anything is read or an access is claimed.
    /// </summary>
    private static async Task<Results<Ok<ContentPackageDetailsResponse>, ProblemHttpResult>> GetPackageAsync(
        [FromRoute] Guid packageId,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromHeader(Name = "X-Unlock-Proof")] string? unlockProof,
        [FromServices] IContentStorageHandler storageHandler,
        [FromServices] IBlobStorageHandler blobStorageHandler,
        [FromServices] IDownloadTokenService tokenService,
        [FromServices] IOptions<DownloadSettings> downloadOptions,
        [FromServices] LinkGenerator linkGenerator,
        [FromServices] TimeProvider timeProvider,
        [FromServices] ShareItMetrics metrics,
        HttpContext httpContext,
        ClaimsPrincipal claimsPrincipal,
        ILogger<LogCategory> logger,
        CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.open");

        if (!claimsPrincipal.TryGetUserIdentity(guestId, out var userIdentity))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "User ID required",
                Detail = "Either an authenticated user or a guest ID must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        activity?.SetTag("shareit.identity.type", userIdentity.IsGuest ? "guest" : "user");

        // Shell first: no items, no names, no tokens, no blob reads.
        var package = await storageHandler.GetPackageShellAsync(packageId, cancellationToken);
        if (package is null || package.IsPrivate.HasValue && package.IsPrivate.Value && (userIdentity.IsGuest || package.UserId != userIdentity.Id))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package not found",
                Detail = $"Package with ID {packageId} was not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        var now = timeProvider.GetUtcNow();
        activity?.SetTag("shareit.package.protected", package.UnlockVerifier is not null);

        // Expiry is not content, so answering 410 before the proof is safe.
        if (package.IsExpired(now))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package expired",
                Detail = "This package has expired and is no longer available",
                Status = StatusCodes.Status410Gone
            });
        }

        // Proof before the access claim: a wrong passphrase costs nothing and
        // reveals nothing. The header value is never logged.
        if (package.UnlockVerifier is not null && !UnlockProof.Matches(package.UnlockVerifier, unlockProof))
        {
            logger.LogWarning("Failed unlock attempt for package {PackageId}", packageId);
            metrics.UnlockFailed();
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Unlock required",
                Detail = "A valid unlock proof is required to view this package.",
                Status = StatusCodes.Status401Unauthorized
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

        metrics.AccessClaimed();

        var withContent = await storageHandler.GetPackageAsync(packageId, includeContent: true, cancellationToken);
        if (withContent?.Contents is null || withContent.Contents.Count == 0)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package is empty",
                Detail = "This package contains no content",
                Status = StatusCodes.Status410Gone
            });
        }

        activity?.SetTag("shareit.package.items", withContent.Contents.Count);

        var inlineLimitBytes = downloadOptions.Value.InlinePayloadLimitBytes;
        var inlineBudgetBytes = downloadOptions.Value.InlinePayloadBudgetBytes;
        var inlineSpentBytes = 0L;

        var items = new List<ContentItemInPackage>();
        var inlineItemCount = 0;
        var tokenItemCount = 0;
        var orderedContents = withContent.Contents
            .OrderBy(content => content.CreatedAt)
            .ThenBy(content => content.Id.ToString("N"), StringComparer.Ordinal);

        foreach (var content in orderedContents)
        {
            var inline = content.Size <= inlineLimitBytes
                         && inlineSpentBytes + content.Size <= inlineBudgetBytes;

            if (inline)
            {
                var dataStream = await blobStorageHandler.DownloadAsync(content.Url, cancellationToken);
                using var buffer = new MemoryStream();
                await dataStream.CopyToAsync(buffer, cancellationToken);
                inlineSpentBytes += content.Size;
                inlineItemCount++;

                items.Add(new ContentItemInPackage
                {
                    Id = content.Id,
                    Kind = KindOf(content.ContentType),
                    Size = content.Size,
                    Meta = content.Meta,
                    Payload = Base64Url.EncodeToString(buffer.ToArray())
                });
            }
            else
            {
                var token = await tokenService.GenerateTokenAsync(content.Id, userIdentity.Id, cancellationToken);
                metrics.TokenIssued();
                tokenItemCount++;
                var downloadUrl = linkGenerator.GetUriByName(
                    httpContext,
                    "DownloadContentWithToken",
                    new { packageId = withContent.Id, contentId = content.Id })!;

                items.Add(new ContentItemInPackage
                {
                    Id = content.Id,
                    Kind = KindOf(content.ContentType),
                    Size = content.Size,
                    Meta = content.Meta,
                    DownloadToken = token.Token,
                    DownloadUrl = downloadUrl,
                    TokenExpiresAt = token.ExpiresAt
                });
            }
        }

        var response = new ContentPackageDetailsResponse
        {
            Package = ContentPackageResponse.FromPackage(withContent),
            Items = items
        };

        activity?.SetTag("shareit.package.inline_items", inlineItemCount);
        activity?.SetTag("shareit.package.token_items", tokenItemCount);
        activity?.SetTag("shareit.package.inline_bytes", inlineSpentBytes);
        activity?.SetTag("shareit.package.result", "opened");

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
        [FromServices] ShareItMetrics metrics,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.download");

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

        activity?.SetTag("shareit.identity.type", userIdentity.IsGuest ? "guest" : "user");

        // Read-only validation first: a failing blob read must not burn the
        // token.
        var validationResult = await tokenService.ValidateTokenAsync(token, userIdentity.Id, cancellationToken);
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
                Title = "Token mismatch",
                Detail = "The download token is not valid for this content",
                Status = StatusCodes.Status400BadRequest
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

        var content = package.Contents.Single(c => c.Id == contentId);

        activity?.SetTag("shareit.content.kind", KindOf(content.ContentType));
        activity?.SetTag("shareit.content.size_bytes", content.Size);

        Stream stream;
        try
        {
            stream = await blobStorageHandler.DownloadAsync(content.Url, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "content not found");
            // The token stays usable: nothing was streamed and nothing consumed.
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Content not found",
                Detail = $"Content with ID {contentId} was not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Consume only once the blob is open. Losing the race (another request
        // used the same token) must not stream anything.
        var consumed = await tokenService.ConsumeTokenAsync(token, userIdentity.Id, cancellationToken);
        if (!consumed.IsValid)
        {
            await stream.DisposeAsync();
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Invalid download token",
                Detail = consumed.ErrorMessage ?? "The download token has already been used",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // The token is consumed and the download is authorized here. The
        // response body is streamed after this method returns, so this is not
        // a completed-transfer measurement.
        metrics.DownloadAuthorized(content.Size);
        activity?.SetTag("shareit.download.authorized", true);

        // No names or MIME types here: both are part of the client-sealed
        // per-item metadata. The browser saves the bytes as an opaque blob.
        return TypedResults.Stream(
            stream,
            "application/octet-stream",
            enableRangeProcessing: true);
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

    private static bool TryParseKind(string? kind, out ContentType contentType)
    {
        contentType = ContentType.File;

        if (string.Equals(kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            contentType = ContentType.Text;
            return true;
        }

        return string.Equals(kind, "file", StringComparison.OrdinalIgnoreCase);
    }

    private static string KindOf(ContentType contentType)
        => contentType == ContentType.Text ? "text" : "file";

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

    private static ProblemHttpResult FileTooLargeProblem(Guid contentId, long fileLimitBytes)
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Title = "File too large",
            Detail = $"Content '{contentId}' exceeds the upload limit of {fileLimitBytes / (1024d * 1024d):0.#} MB.",
            Status = StatusCodes.Status413PayloadTooLarge
        });
    }
}
