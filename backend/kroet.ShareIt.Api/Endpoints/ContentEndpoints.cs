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
using static System.Net.Mime.MediaTypeNames;

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
            .MapGet("/{packageId:guid}/content/{contentId:guid}/download", DownloadContentAsync)
            .RequirePermission("Download.File")
            .RequireRateLimiting(RateLimitPolicies.Download)
            // .NET 11 describes FileStreamHttpResult as a binary string schema.
            .Produces<FileStreamHttpResult>(contentType: Application.Octet)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status410Gone)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("DownloadContent")
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

        var accessCountProblem = ValidateMaxAccessCount(userIdentity, settings, request.MaxAccessCount);
        if (accessCountProblem is not null)
        {
            return accessCountProblem;
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
            MaxAccessCount = request.MaxAccessCount,
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
    /// Gets a package with all its content items and returns its access session.
    /// Items above the inline payload limit are downloaded through that session.
    /// A protected package requires a valid unlock proof before anything is read
    /// or an access is claimed.
    /// </summary>
    private static async Task<Results<Ok<ContentPackageDetailsResponse>, ProblemHttpResult>> GetPackageAsync(
        [FromRoute] Guid packageId,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromHeader(Name = "X-Unlock-Proof")] string? unlockProof,
        [FromHeader(Name = "X-Access-Session")] string? accessSession,
        [FromServices] IContentStorageHandler storageHandler,
        [FromServices] IBlobStorageHandler blobStorageHandler,
        [FromServices] IAccessSessionService accessSessionService,
        [FromServices] IOptions<DownloadSettings> downloadOptions,
        [FromServices] TimeProvider timeProvider,
        [FromServices] ShareItMetrics metrics,
        ClaimsPrincipal claimsPrincipal,
        HttpContext httpContext,
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

        // Shell first: no items, no names, no session, no blob reads.
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

        // Content metadata is safe to read after expiry/proof validation. Check
        // it before claiming so an unfinished package cannot burn its only view.
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

        // A session is read-only validation. If it is valid, this re-fetch does
        // not consume another access; if it is missing or stale, the claim
        // below creates a new one after preparation succeeds.
        AccessSessionTicket? existingSession = null;
        if (!string.IsNullOrWhiteSpace(accessSession))
        {
            existingSession = await accessSessionService.ValidateAsync(
                accessSession,
                packageId,
                userIdentity.Id,
                userIdentity.IsGuest,
                cancellationToken);
            activity?.SetTag("shareit.access.session_reused", existingSession is not null);
        }

        // An exhausted package with no valid session cannot succeed below, and
        // answering 410 here avoids reading and buffering inline S3 payloads
        // (or turning a missing object into a 500) for a request that is
        // already known to fail.
        if (existingSession is null && withContent.IsAccessLimitReached)
        {
            return AccessLimitReachedProblem();
        }

        var inlineLimitBytes = downloadOptions.Value.InlinePayloadLimitBytes;
        var inlineBudgetBytes = downloadOptions.Value.InlinePayloadBudgetBytes;
        var inlineSpentBytes = 0L;

        // C# 15 collection expression argument: pre-size the list because the
        // number of items is already known.
        List<ContentItemInPackage> items = [with(capacity: withContent.Contents.Count)];
        var inlineItemCount = 0;
        var deferredItemCount = 0;
        var orderedContents = withContent.Contents
            .OrderBy(content => content.CreatedAt)
            .ThenBy(content => content.Id.ToString("N"), StringComparer.Ordinal);

        foreach (var content in orderedContents)
        {
            var inline = content.Size <= inlineLimitBytes
                         && inlineSpentBytes + content.Size <= inlineBudgetBytes;

            if (inline)
            {
                await using var dataStream = await blobStorageHandler.DownloadAsync(content.Url, cancellationToken);
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
                    // Encode directly from the buffer instead of copying it
                    // into another array first.
                    Payload = Base64Url.EncodeToString(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))
                });
            }
            else
            {
                // Nothing is minted here: the package's access session already
                // authorizes a lazy download when the recipient clicks the item.
                deferredItemCount++;
                items.Add(new ContentItemInPackage
                {
                    Id = content.Id,
                    Kind = KindOf(content.ContentType),
                    Size = content.Size,
                    Meta = content.Meta
                });
            }
        }

        // Claim last: all response preparation above can still fail without
        // consuming an access, and the counter plus session commit together.
        AccessSessionTicket ticket;
        var accessClaimed = false;

        if (existingSession is not null)
        {
            ticket = existingSession;
            metrics.AccessSessionReused();
        }
        else
        {
            var creation = await accessSessionService.CreateAsync(
                packageId,
                userIdentity.Id,
                userIdentity.IsGuest,
                cancellationToken);

            if (!creation.Success || creation.Ticket is null)
            {
                return AccessLimitReachedProblem();
            }

            ticket = creation.Ticket;
            accessClaimed = true;
            metrics.AccessClaimed();

            // The response must reflect the access that was just claimed. The
            // package was loaded before the claim, so its counter is stale.
            withContent = withContent with { CurrentAccessCount = creation.NewAccessCount };
        }

        var response = new ContentPackageDetailsResponse
        {
            Package = ContentPackageResponse.FromPackage(withContent),
            Items = items,
            AccessSession = new AccessSessionResponse
            {
                Token = ticket.Token,
                ExpiresAt = ticket.ExpiresAt
            }
        };

        activity?.SetTag("shareit.package.inline_items", inlineItemCount);
        activity?.SetTag("shareit.package.deferred_items", deferredItemCount);
        activity?.SetTag("shareit.package.inline_bytes", inlineSpentBytes);
        activity?.SetTag("shareit.package.access_claimed", accessClaimed);
        activity?.SetTag("shareit.package.result", "opened");

        // The body carries the access session token.
        httpContext.Response.Headers.CacheControl = "no-store";

        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Downloads content data using the package's access session. The session
    /// stays valid, so retries and parallel downloads never consume another
    /// access.
    /// </summary>
    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> DownloadContentAsync(
        [FromRoute] Guid packageId,
        [FromRoute] Guid contentId,
        [FromHeader(Name = "X-Access-Session")] string? accessSession,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler storageHandler,
        [FromServices] IBlobStorageHandler blobStorageHandler,
        [FromServices] IAccessSessionService accessSessionService,
        [FromServices] TimeProvider timeProvider,
        [FromServices] ShareItMetrics metrics,
        ClaimsPrincipal claimsPrincipal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.download");

        if (string.IsNullOrWhiteSpace(accessSession))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Missing access session",
                Detail = "An access session must be provided in the X-Access-Session header",
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

        // Read-only validation: downloads never consume the session.
        var session = await accessSessionService.ValidateAsync(
            accessSession,
            packageId,
            userIdentity.Id,
            userIdentity.IsGuest,
            cancellationToken);

        if (session is null)
        {
            // The session row cascades away when its package is deleted. In
            // that case 401 would be misleading; distinguish a dead package
            // from a merely expired/invalid session.
            var packageShell = await storageHandler.GetPackageShellAsync(packageId, cancellationToken);
            if (packageShell is null)
            {
                return TypedResults.Problem(new ProblemDetails
                {
                    Title = "Package not found",
                    Detail = $"Package with ID {packageId} was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            if (packageShell.IsExpired(timeProvider.GetUtcNow()))
            {
                return TypedResults.Problem(new ProblemDetails
                {
                    Title = "Package expired",
                    Detail = "This package has expired and is no longer available",
                    Status = StatusCodes.Status410Gone
                });
            }

            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Invalid access session",
                Detail = "The access session is invalid or has expired",
                Status = StatusCodes.Status401Unauthorized
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

        if (package.IsExpired(timeProvider.GetUtcNow()))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package expired",
                Detail = "This package has expired and is no longer available",
                Status = StatusCodes.Status410Gone
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
            // The session stays valid: nothing was streamed.
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Content not found",
                Detail = $"Content with ID {contentId} was not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        // The session authorizes the download. The response body is streamed
        // after this method returns, so this is not a completed-transfer
        // measurement.
        metrics.DownloadAuthorized(content.Size);
        activity?.SetTag("shareit.download.authorized", true);

        // A session can serve repeated requests; shared caches must not keep
        // the response.
        httpContext.Response.Headers.CacheControl = "no-store";

        // No names or MIME types here: both are part of the client-sealed
        // per-item metadata. The browser saves the bytes as an opaque blob.
        return TypedResults.Stream(stream, "application/octet-stream");
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
            AnonymousMaxAccessCount = settings.Anonymous.MaxAccessCount,
            AuthenticatedMaxAccessCount = settings.Authenticated.MaxAccessCount
        });
    }

    private static ProblemHttpResult AccessLimitReachedProblem()
        => TypedResults.Problem(new ProblemDetails
        {
            Title = "Package access limit reached",
            Detail = "The maximum number of accesses for this package has been reached",
            Status = StatusCodes.Status410Gone
        });

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

    private static ProblemHttpResult? ValidateMaxAccessCount(
        UserIdentity userIdentity, UploadSettings settings, int? maxAccessCount)
    {
        if (maxAccessCount is { } count)
        {
            if (count <= 0)
            {
                return BadRequestProblem(
                    "Invalid access limit",
                    "MaxAccessCount must be a positive number.");
            }

            var identityLimits = userIdentity.IsGuest ? settings.Anonymous : settings.Authenticated;
            if (count > identityLimits.MaxAccessCount)
            {
                return BadRequestProblem(
                    "Access limit too high",
                    $"Packages for this identity allow at most {identityLimits.MaxAccessCount} accesses.");
            }

            return null;
        }

        if (userIdentity.IsGuest)
        {
            return BadRequestProblem(
                "Access limit required",
                $"Anonymous packages must set MaxAccessCount of at most {settings.Anonymous.MaxAccessCount}.");
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
