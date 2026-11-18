using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using System.Text;
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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
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
    /// Extra bytes allowed for the final multipart boundary and headers when a
    /// duplicate upload is drained before its idempotent response.
    /// </summary>
    private const long DrainTrailerAllowanceBytes = 64 * 1024;

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
            .Produces<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)
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
            // The handler writes the stream manually and returns EmptyHttpResult,
            // but this result type keeps the OpenAPI body schema binary.
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
    /// <remarks>
    /// The body is a <c>multipart/form-data</c> request whose parts are ordered:
    /// <c>Id</c>, <c>Kind</c>, <c>Meta</c>, <c>Size</c>, then <c>File</c> last.
    /// Metadata parts are validated before the payload is read, and duplicate
    /// metadata parts are rejected before storage. The endpoint buffers only
    /// the small metadata parts; the payload is streamed through the blob
    /// store's bounded probe/part buffer and is never buffered on disk.
    /// </remarks>
    private static async Task<Results<Ok<ContentPackageResponse>, ProblemHttpResult, EmptyHttpResult>>
        UploadContentToPackageAsync(
        [FromRoute] Guid packageId,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] IStorageHandler storageHandler,
        [FromServices] TimeProvider timeProvider,
        [FromServices] IOptions<UploadSettings> uploadOptions,
        [FromServices] ShareItMetrics metrics,
        ClaimsPrincipal claimsPrincipal,
        HttpContext httpContext,
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

        if (!TryGetMultipartBoundary(httpContext.Request, out var boundary))
        {
            return BadRequestProblem(
                "Invalid content type",
                "The upload must be sent as multipart/form-data with a boundary.");
        }

        var settings = uploadOptions.Value;
        var fileLimitBytes = FileLimitBytes(userIdentity, settings);

        // Metadata errors are answered before the payload is read. Some clients
        // may surface that early response as a network error, but draining up
        // to Kestrel's request-body limit for every malformed request would be
        // worse. Only a matching idempotent retry drains, because that one has
        // already succeeded and must not look like a failed upload.
        var reader = new MultipartReader(boundary, httpContext.Request.Body);
        Guid? contentId = null;
        string? kind = null;
        string? meta = null;
        long? declaredSize = null;
        var seenParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            MultipartSection? section;

            try
            {
                section = await reader.ReadNextSectionAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                return TypedResults.Empty;
            }
            catch (InvalidDataException)
            {
                // Malformed multipart framing must be a client error, not a 500.
                return BadRequestProblem("Invalid upload", "The multipart body is invalid.");
            }
            catch (IOException)
            {
                // The body ended before its final boundary.
                return BadRequestProblem("Invalid upload", "The multipart body ended unexpectedly.");
            }

            if (section is null)
            {
                break;
            }

            if (!TryGetPartName(section, out var partName))
            {
                return BadRequestProblem("Invalid upload", "Each part must be form-data with a name.");
            }

            if (!seenParts.Add(partName))
            {
                return BadRequestProblem("Invalid upload", $"Duplicate part '{partName}'.");
            }

            // Metadata must arrive first: the checks below run before a single
            // payload byte is read, and a duplicate retry never stores bytes.
            if (!string.Equals(partName, "File", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetPartTextLimit(partName, out var partLimit))
                {
                    return BadRequestProblem("Invalid upload", $"Unknown part '{partName}'.");
                }

                string? value;
                try
                {
                    value = await ReadPartTextAsync(section.Body, partLimit, cancellationToken);
                }
                catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
                {
                    return TypedResults.Empty;
                }
                catch (InvalidDataException)
                {
                    return BadRequestProblem("Invalid upload", "The multipart body is invalid.");
                }
                catch (IOException)
                {
                    return BadRequestProblem("Invalid upload", "The multipart body ended unexpectedly.");
                }

                if (value is null)
                {
                    return BadRequestProblem("Invalid upload", $"The {partName} part is too long.");
                }

                switch (partName.ToLowerInvariant())
                {
                    case "id":
                        if (!Guid.TryParse(value, out var parsedId) || parsedId == Guid.Empty)
                        {
                            return BadRequestProblem("Content id required", "The client must supply a non-empty content id.");
                        }

                        contentId = parsedId;
                        break;
                    case "kind":
                        kind = value;
                        break;
                    case "meta":
                        meta = value;
                        break;
                    case "size":
                        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSize)
                            || parsedSize < 0)
                        {
                            return BadRequestProblem("Invalid size", "Size must be a non-negative number of bytes.");
                        }

                        declaredSize = parsedSize;
                        break;
                }

                continue;
            }

            if (contentId is not { } itemId || itemId == Guid.Empty)
            {
                return BadRequestProblem("Content id required", "The client must supply a non-empty content id.");
            }

            if (!TryParseKind(kind, out var contentType))
            {
                return BadRequestProblem("Invalid kind", "Kind must be either 'text' or 'file'.");
            }

            if (meta is null || !Base64UrlText.TryDecode(meta, out var metaBytes))
            {
                return BadRequestProblem("Invalid meta", "Meta must be base64url text.");
            }

            if (metaBytes.Length > UnlockProof.MaxMetaBytes)
            {
                return BadRequestProblem(
                    "Meta too large",
                    $"Meta must decode to at most {UnlockProof.MaxMetaBytes} bytes.");
            }

            if (declaredSize is not { } itemSize)
            {
                return BadRequestProblem("Invalid size", "Size must be a non-negative number of bytes.");
            }

            if (itemSize == 0)
            {
                return BadRequestProblem("Empty content", "The uploaded content must not be empty.");
            }

            if (itemSize > fileLimitBytes)
            {
                return FileTooLargeProblem(itemId, fileLimitBytes);
            }

            activity?.SetTag("shareit.content.kind", KindOf(contentType));
            activity?.SetTag("shareit.content.size_bytes", itemSize);

            // Idempotent retry: a repeated upload with the same item id must not
            // create a duplicate row or fail on the primary key. Reusing the id
            // for different content is a conflict, not a silent success.
            var existing = await contentStorageHandler.GetContentAsync(itemId, cancellationToken);
            if (existing is not null)
            {
                // Decide conflicts before reading the payload. Only an
                // idempotent success drains the re-sent body so the client can
                // receive the response instead of a connection reset.
                if (existing.PackageId != packageId)
                {
                    return TypedResults.Problem(new ProblemDetails
                    {
                        Title = "Content id already in use",
                        Detail = "Content with this id already exists in another package.",
                        Status = StatusCodes.Status409Conflict
                    });
                }

                if (existing.ContentType != contentType
                    || existing.Size != itemSize
                    || !string.Equals(existing.Meta, meta, StringComparison.Ordinal))
                {
                    return TypedResults.Problem(new ProblemDetails
                    {
                        Title = "Content id reused with different content",
                        Detail = "This content id was already used for an item with different kind, size or metadata.",
                        Status = StatusCodes.Status409Conflict
                    });
                }

                await DrainRequestBodyAsync(
                    httpContext.Request,
                    itemSize + DrainTrailerAllowanceBytes,
                    cancellationToken);

                var existingPackage = await contentStorageHandler.GetPackageAsync(packageId, true, cancellationToken);
                activity?.SetTag("shareit.package.result", "existing");
                return TypedResults.Ok(ContentPackageResponse.FromPackage(existingPackage ?? package));
            }

            var now = timeProvider.GetUtcNow();
            var content = new Api.Models.Content
            {
                Id = itemId,
                PackageId = packageId,
                ContentType = contentType,
                CreatedAt = now,
                Size = itemSize,
                UserId = userIdentity.Id,
                Meta = meta
            };

            await using var payload = new DeclaredLengthStream(section.Body, fileLimitBytes, itemSize);

            try
            {
                await storageHandler.StoreContentAsync(content, payload, cancellationToken);
            }
            catch (ContentTooLargeException)
            {
                // The client sent more bytes than its limit allows, whatever it
                // declared. Storage compensation already removed any partial blob.
                return FileTooLargeProblem(itemId, fileLimitBytes);
            }
            catch (ContentLengthMismatchException)
            {
                // The stream stopped forwarding at the first extra byte. Drain
                // the rest only to preserve the 413 answer when the client also
                // exceeded the absolute identity limit.
                bool exceededLimit;
                try
                {
                    exceededLimit = await payload.DrainToLimitAsync(cancellationToken);
                }
                catch (RequestBodyTruncatedException)
                {
                    return BadRequestProblem("Invalid upload", "The multipart body ended unexpectedly.");
                }
                catch (InvalidDataException)
                {
                    return BadRequestProblem("Invalid upload", "The multipart body is invalid.");
                }
                catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
                {
                    return TypedResults.Empty;
                }

                return exceededLimit
                    ? FileTooLargeProblem(itemId, fileLimitBytes)
                    : BadRequestProblem(
                        "Content size mismatch",
                        "The uploaded content length does not match the declared size.");
            }
            catch (RequestBodyTruncatedException)
            {
                return BadRequestProblem("Invalid upload", "The multipart body ended unexpectedly.");
            }
            catch (InvalidDataException)
            {
                return BadRequestProblem("Invalid upload", "The multipart body is invalid.");
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                return TypedResults.Empty;
            }

            // File is contractually the last part. A well-formed trailing
            // section means the client violated the contract; remove the
            // just-stored item so the 400 is not misleading.
            try
            {
                var trailing = await reader.ReadNextSectionAsync(cancellationToken);
                if (trailing is not null)
                {
                    await DeleteRejectedUploadAsync(storageHandler, itemId);
                    return BadRequestProblem("Invalid upload", "The File part must be the last part.");
                }
            }
            catch (InvalidDataException)
            {
                await DeleteRejectedUploadAsync(storageHandler, itemId);
                return BadRequestProblem("Invalid upload", "The multipart body is invalid.");
            }
            catch (IOException)
            {
                await DeleteRejectedUploadAsync(storageHandler, itemId);
                return BadRequestProblem("Invalid upload", "The multipart body ended unexpectedly.");
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                return TypedResults.Empty;
            }

            metrics.ItemUploaded(contentType, itemSize);
            activity?.SetTag("shareit.package.result", "uploaded");

            var updatedPackage = await contentStorageHandler.GetPackageAsync(packageId, true, cancellationToken);

            return TypedResults.Ok(ContentPackageResponse.FromPackage(updatedPackage ?? package));
        }

        return BadRequestProblem("Empty content", "The uploaded content must not be empty.");
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
    private static async Task<Results<EmptyHttpResult, ProblemHttpResult>> DownloadContentAsync(
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
        httpContext.Response.ContentType = Application.Octet;
        // The stored size is authoritative; sending it lets the browser show
        // real progress and detect a truncated transfer.
        httpContext.Response.ContentLength = content.Size;

        try
        {
            // Copy explicitly instead of returning a file result: this keeps
            // the S3 stream's lifetime explicit (it is disposed with its
            // connection), and a client disconnect cancels the transfer
            // instead of surfacing as a server error.
            await stream.CopyToAsync(httpContext.Response.Body, 64 * 1024, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away mid-download; nothing was persisted.
        }
        catch (IOException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The connection broke while writing; also a client-side stop.
        }
        catch (Exception) when (!httpContext.Response.HasStarted)
        {
            // Nothing was written yet: let the error handler replace the
            // streamed response instead of leaving a stale content length.
            httpContext.Response.ContentLength = null;
            throw;
        }
        finally
        {
            await stream.DisposeAsync();
        }

        return TypedResults.Empty;
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

    /// <summary>
    /// Reads the boundary of a <c>multipart/form-data</c> request. Without a
    /// boundary, <see cref="MultipartReader"/> would scan the body with a
    /// delimiter the client never sent.
    /// </summary>
    private static bool TryGetMultipartBoundary(HttpRequest request, [NotNullWhen(true)] out string? boundary)
    {
        boundary = null;

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // RFC 2046 allows a quoted boundary parameter. MediaTypeHeaderValue
        // preserves those quotes, but MultipartReader expects the raw value.
        var boundarySegment = HeaderUtilities.RemoveQuotes(contentType.Boundary);
        if (!boundarySegment.HasValue || boundarySegment.Length is 0 or > 70)
        {
            return false;
        }

        boundary = boundarySegment.Value;
        return true;
    }

    /// <summary>
    /// Extracts the form-data part name. Other dispositions (attachments, a
    /// missing name) are rejected before their content is read.
    /// </summary>
    private static bool TryGetPartName(MultipartSection section, [NotNullWhen(true)] out string? partName)
    {
        partName = null;

        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
            || !disposition.Name.HasValue)
        {
            return false;
        }

        partName = disposition.Name.Value;
        return !string.IsNullOrEmpty(partName);
    }

    /// <summary>
    /// Per-part read limit for the small text parts. The meta limit leaves
    /// room for base64url expansion of the maximum decoded size.
    /// </summary>
    private static bool TryGetPartTextLimit(string partName, out int limitBytes)
    {
        limitBytes = partName.ToLowerInvariant() switch
        {
            "id" => 64,
            "kind" => 16,
            "meta" => (UnlockProof.MaxMetaBytes * 4 / 3) + 8,
            "size" => 20,
            _ => 0
        };

        return limitBytes > 0;
    }

    /// <summary>
    /// Reads and discards at most <paramref name="maxBytes"/> of the remaining
    /// request body before an idempotent response. Closing the connection while
    /// the client is still uploading can surface as a network error, which
    /// would make a client retry an upload the server has already accepted.
    /// The bound prevents a duplicate request from making the server read up to
    /// Kestrel's whole request-body limit.
    /// </summary>
    private static async Task DrainRequestBodyAsync(
        HttpRequest request,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var drained = 0L;

            try
            {
                while (drained < maxBytes)
                {
                    var readSize = (int)Math.Min(buffer.Length, maxBytes - drained);
                    var read = await request.Body.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    drained += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client stopped sending; the response is still the best answer.
        }
        catch (IOException)
        {
            // The connection broke; nothing left to drain.
        }
    }

    /// <summary>
    /// Removes an item that was stored before a trailing multipart section was
    /// discovered. Best effort only: the 400 is still the correct response, and
    /// any leftover object is handled by the normal cleanup queue.
    /// </summary>
    private static async Task DeleteRejectedUploadAsync(IStorageHandler storageHandler, Guid contentId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await storageHandler.DeleteContentAsync(contentId, timeout.Token);
        }
        catch
        {
            // Best effort; the request must still report the contract error.
        }
    }

    /// <summary>
    /// Reads a small text part, returning null when it is longer than the
    /// limit so a hostile client cannot make the endpoint buffer metadata.
    /// </summary>
    private static async Task<string?> ReadPartTextAsync(
        Stream body,
        int limitBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[limitBytes + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await body.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total > limitBytes ? null : Encoding.UTF8.GetString(buffer, 0, total).Trim();
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
