using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Services;
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
    /// Maps all content management endpoints to the route group.
    /// </summary>
    public static RouteGroupBuilder MapContentEndpoints(this RouteGroupBuilder group)
    {
        var packageGroup = group.MapGroup("/package");

        // Upload limits endpoint (placed before the parameterized routes)
        packageGroup
            .MapGet("/limits", GetUploadLimits)
            .Produces<UploadLimitsResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .WithName("GetUploadLimits")
            .WithTags("Content", "Package");

        // Package creation: metadata only. Content is added one item per
        // request through PUT /package/{id}/content/{contentId}, which is the
        // single content write path. Code-mode packages get their server-side
        // locator here; the client secret is never part of the request.
        packageGroup
            .MapPost("", CreatePackageAsync)
            .RequireRateLimiting(RateLimitPolicies.Upload)
            .Accepts<CreatePackageRequest>("application/json")
            .Produces<ContentPackageOwnerResponse>(StatusCodes.Status201Created)
            .Produces<ContentPackageOwnerResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status410Gone)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)
            .WithName("CreatePackage")
            .WithTags("Content", "Package");

        // One raw binary PUT per item. The payload is streamed from
        // Request.Body through the blob store's bounded probe/part buffer and
        // is never buffered on disk or in memory. The binary request body is
        // described in OpenAPI through the document transformer in Program.cs.
        packageGroup
            .MapPut("/{packageId:guid}/content/{contentId:guid}", UploadContentToPackageAsync)
            .RequireRateLimiting(RateLimitPolicies.Upload)
            .Produces<ContentPackageOwnerResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status410Gone)
            .Produces<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)
            .Produces<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("UploadContent")
            .WithTags("Content", "Package");

        // The short code locator is only a lookup alias. It does not claim an
        // access and it does not check the unlock proof; the client then opens
        // the package through the canonical id. The locator travels in the body
        // so it never lands in access logs or browser history.
        packageGroup
            .MapPost("/code/resolve", ResolveShortCodeAsync)
            .RequireRateLimiting(RateLimitPolicies.ShortCodeLookup)
            .Accepts<ResolveShortCodeRequest>("application/json")
            .Produces<ResolveShortCodeResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("ResolveShortCode")
            .WithTags("Content", "Package");

        packageGroup
            .MapGet("/{packageId:guid}", GetPackageAsync)
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

        // Content download endpoint: every item's bytes are fetched here with
        // the package's access session. Nothing is inlined in GET /package.
        packageGroup
            .MapGet("/{packageId:guid}/content/{contentId:guid}/download", DownloadContentAsync)
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
    /// Resolves a three-character short-code locator to the canonical package
    /// id. This endpoint never claims an access, checks a passphrase or loads
    /// content; the client then opens the package through the normal id-based
    /// flow with the three-character client secret it kept locally.
    /// </summary>
    private static async Task<Results<Ok<ResolveShortCodeResponse>, ProblemHttpResult>> ResolveShortCodeAsync(
        [FromBody] ResolveShortCodeRequest request,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] TimeProvider timeProvider,
        [FromServices] ShareItMetrics metrics,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.resolve_code");
        httpContext.Response.Headers.CacheControl = "no-store";

        var normalized = ShortCode.Normalize(request.Code);
        if (normalized is null)
        {
            metrics.ShortCodeLookup("invalid");
            activity?.SetTag("shareit.short_code.result", "invalid");
            return ShortCodeNotFoundProblem();
        }

        var package = await contentStorageHandler.GetPackageShellByShortCodeAsync(normalized, cancellationToken);
        if (package is null
            || package.IsExpired(timeProvider.GetUtcNow())
            || package.IsAccessLimitReached)
        {
            metrics.ShortCodeLookup("not_found");
            activity?.SetTag("shareit.short_code.result", "not_found");
            return ShortCodeNotFoundProblem();
        }

        metrics.ShortCodeLookup("found");
        activity?.SetTag("shareit.short_code.result", "found");
        return TypedResults.Ok(new ResolveShortCodeResponse { PackageId = package.Id });
    }

    private static ProblemHttpResult ShortCodeNotFoundProblem()
        => TypedResults.Problem(new ProblemDetails
        {
            Title = "Short code not found",
            Detail = "No package has this short code.",
            Status = StatusCodes.Status404NotFound
        });

    private static ProblemHttpResult ShortCodeUnavailableProblem()
        => TypedResults.Problem(new ProblemDetails
        {
            Title = "Short code unavailable",
            Detail = "A short code could not be allocated. Please retry the request.",
            Status = StatusCodes.Status503ServiceUnavailable
        });

    private static ProblemHttpResult PackageIdConflictProblem(string detail)
        => TypedResults.Problem(new ProblemDetails
        {
            Title = "Package id already in use",
            Detail = detail,
            Status = StatusCodes.Status409Conflict
        });

    /// <summary>
    /// A regular package retry keeps the historical idempotency behavior. A
    /// code-mode retry must match the mode and the verifier the package was
    /// created with; otherwise the client would receive a locator that does
    /// not unlock the stored verifier.
    /// </summary>
    private static bool IsIdempotentRetry(ContentPackage existing, CreatePackageRequest request)
    {
        if (request.UseShortCode != (existing.ShortCode is not null))
        {
            return false;
        }

        if (!request.UseShortCode)
        {
            return true;
        }

        return existing.UnlockVerifier is not null
               && UnlockProof.Matches(existing.UnlockVerifier, request.UnlockVerifier);
    }

    /// <summary>
    /// Creates a new (empty) content package. The client supplies the id
    /// because the salt and verifier derive from it. A new package mints the
    /// upload token returned in the response; repeating the request with the
    /// same id, the same owner and that token is idempotent. Code-mode retries
    /// must also match the verifier, and they require the client to prove it
    /// knows the passphrase before the server allocates the three-character
    /// locator.
    /// </summary>
    private static async Task<Results<Created<ContentPackageOwnerResponse>, Ok<ContentPackageOwnerResponse>, ProblemHttpResult>>
        CreatePackageAsync(
            [FromBody] CreatePackageRequest request,
            [FromHeader(Name = "X-Upload-Token")] string? uploadToken,
            [FromHeader(Name = "X-Unlock-Proof")] string? unlockProof,
            [FromServices] IContentStorageHandler contentStorageHandler,
            [FromServices] IShortCodeGenerator shortCodeGenerator,
            [FromServices] TimeProvider timeProvider,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] IOptions<UploadSettings> uploadOptions,
            [FromServices] ShareItMetrics metrics,
            [FromServices] ILogger<LogCategory> logger,
            ClaimsPrincipal claimsPrincipal,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.create");

        var userId = claimsPrincipal.GetUserId();
        activity?.SetTag("shareit.identity.type", userId is null ? "guest" : "user");

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

        // Code-mode packages are always protected: the browser derives the key
        // from the client-held secret and proves knowledge before creation.
        if (request.UseShortCode)
        {
            if (request.UnlockVerifier is null)
            {
                return BadRequestProblem(
                    "Unlock verifier required",
                    "Code-mode packages must be encrypted and carry an UnlockVerifier.");
            }

            if (!UnlockProof.Matches(request.UnlockVerifier, unlockProof))
            {
                return BadRequestProblem(
                    "Unlock proof required",
                    "Code-mode packages require a matching X-Unlock-Proof header.");
            }
        }

        var settings = uploadOptions.Value;
        int? maxAccessCount;
        DateTimeOffset expiresAt;
        var now = timeProvider.GetUtcNow();

        if (request.UseShortCode)
        {
            // Fixed guardrails for guests and authenticated users alike: the
            // requested lifetime and access count are ignored.
            maxAccessCount = 1;
            expiresAt = now.Add(ShortCode.PackageLifetime);
        }
        else
        {
            var lifetimeProblem = ValidateLifetime(userId, settings, request.TimeToLiveHours);
            if (lifetimeProblem is not null)
            {
                return lifetimeProblem;
            }

            var accessCountProblem = ValidateMaxAccessCount(userId, settings, request.MaxAccessCount);
            if (accessCountProblem is not null)
            {
                return accessCountProblem;
            }

            maxAccessCount = request.MaxAccessCount;
            expiresAt = now.AddHours(request.TimeToLiveHours);
        }

        // Idempotent retry: the same owner repeating the create call with the
        // upload token it was given gets the existing package back. Another
        // identity or a missing/wrong token must not probe or hijack the id.
        var existing = await contentStorageHandler.GetPackageShellAsync(packageId, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != userId
                || !OpaqueToken.Matches(existing.UploadTokenHash, uploadToken))
            {
                return PackageIdConflictProblem("A package with this id already exists.");
            }

            if (existing.IsExpired(now))
            {
                return TypedResults.Problem(new ProblemDetails
                {
                    Title = "Package expired",
                    Detail = "This package has expired and is no longer available",
                    Status = StatusCodes.Status410Gone
                });
            }

            if (!IsIdempotentRetry(existing, request))
            {
                return PackageIdConflictProblem(
                    "This package id was already created with a different access mode or passphrase.");
            }

            if (request.UseShortCode)
            {
                metrics.ShortCodeAllocation("existing");
                activity?.SetTag("shareit.short_code.result", "existing");
            }

            activity?.SetTag("shareit.package.result", "existing");
            // Echo the token so a retried request keeps the same upload scope.
            httpContext.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(ContentPackageOwnerResponse.FromPackage(existing, uploadToken));
        }

        var mintedUploadToken = OpaqueToken.Create();

        var package = new ContentPackage
        {
            Id = packageId,
            CreatedAt = now,
            UserId = userId,
            UploadTokenHash = OpaqueToken.Hash(mintedUploadToken),
            MaxAccessCount = maxAccessCount,
            ExpiresAt = expiresAt,
            UnlockVerifier = request.UnlockVerifier
        };

        try
        {
            if (request.UseShortCode)
            {
                var stored = false;
                for (var attempt = 1; attempt <= ShortCode.MaxGenerationAttempts; attempt++)
                {
                    var shortCode = ShortCode.Normalize(shortCodeGenerator.Next());
                    if (shortCode is null)
                    {
                        metrics.ShortCodeAllocation("invalid_generator");
                        logger.LogError("Short code generator returned an invalid locator");
                        activity?.SetTag("shareit.short_code.result", "invalid_generator");
                        activity?.SetStatus(ActivityStatusCode.Error, "short code allocation failed");
                        return ShortCodeUnavailableProblem();
                    }

                    try
                    {
                        var candidate = package with { ShortCode = shortCode };
                        await contentStorageHandler.StorePackageAsync(candidate, cancellationToken);
                        // The creation response must carry the locator the
                        // client appends its secret to.
                        package = candidate;
                        stored = true;
                        break;
                    }
                    catch (ShortCodeConflictException)
                    {
                        // Another active package owns this locator; try a fresh one.
                    }
                }

                if (!stored)
                {
                    metrics.ShortCodeAllocation("exhausted");
                    logger.LogWarning("Short code allocation exhausted for package {PackageId}", packageId);
                    activity?.SetStatus(ActivityStatusCode.Error, "short code allocation failed");
                    return ShortCodeUnavailableProblem();
                }

                metrics.ShortCodeAllocation("created");
                activity?.SetTag("shareit.short_code.result", "created");
            }
            else
            {
                await contentStorageHandler.StorePackageAsync(package, cancellationToken);
            }
        }
        catch (PackageIdConflictException)
        {
            // Another request won the same-id insert race. That request minted
            // its own upload token, so this caller cannot hold a matching one:
            // the idempotent retry above can never succeed once the insert has
            // lost. Returning its conflict is the only reachable outcome, so
            // there is no second re-read to apply the pre-check rules to.
            return PackageIdConflictProblem("A package with this id already exists.");
        }

        metrics.PackageCreated();
        activity?.SetTag("shareit.package.result", "created");

        var packageUrl = linkGenerator.GetUriByName(httpContext, "GetPackage", new { packageId = package.Id })!;

        httpContext.Response.Headers.CacheControl = "no-store";
        return TypedResults.Created(
            packageUrl,
            ContentPackageOwnerResponse.FromPackage(package, mintedUploadToken));
    }

    /// <summary>
    /// Uploads one item — text bytes or file bytes — to an existing package
    /// as a raw binary PUT body. The bytes and the opaque metadata are stored
    /// verbatim; the server never parses the metadata.
    /// </summary>
    /// <remarks>
    /// The request carries the upload scope in <c>X-Upload-Token</c>, the
    /// metadata in the <c>X-Content-Kind</c> and <c>X-Content-Meta</c> headers
    /// and the payload as a standard <c>application/octet-stream</c> body with
    /// a <c>Content-Length</c>. The token, metadata and length are validated
    /// before the payload is read; the body then streams through the blob
    /// store's bounded probe/part buffer.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The payload is disposed by the enclosing 'await using' declaration on every path.")]
    private static async Task<Results<Ok<ContentPackageOwnerResponse>, ProblemHttpResult, EmptyHttpResult>>
        UploadContentToPackageAsync(
            [FromRoute] Guid packageId,
            [FromRoute] Guid contentId,
            [FromHeader(Name = "X-Content-Kind"), Required] string? kind,
            [FromHeader(Name = "X-Content-Meta"), Required] string? meta,
            [FromHeader(Name = "Content-Length"), Required] string? declaredLength,
            [FromHeader(Name = "X-Upload-Token"), Required] string? uploadToken,
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

        var userId = claimsPrincipal.GetUserId();
        activity?.SetTag("shareit.identity.type", userId is null ? "guest" : "user");

        if (string.IsNullOrWhiteSpace(uploadToken))
        {
            return BadRequestProblem(
                "Missing upload token",
                "An upload token must be provided in the X-Upload-Token header.");
        }

        // The token is required here, so a value of the wrong shape is a client
        // bug worth naming rather than a silent 403 further down. Create is the
        // exception: its token is optional and only consulted when reusing a
        // package id, so a malformed one simply fails to match (409).
        if (!OpaqueToken.IsWellFormed(uploadToken))
        {
            return BadRequestProblem(
                "Invalid upload token",
                $"The upload token must be {OpaqueToken.WellFormedLength} base64url characters.");
        }

        if (contentId == Guid.Empty)
        {
            return BadRequestProblem("Content id required", "The client must supply a non-empty content id.");
        }

        if (!MediaTypeHeaderValue.TryParse(httpContext.Request.ContentType, out var requestContentType)
            || !string.Equals(
                requestContentType.MediaType.Value,
                "application/octet-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Invalid content type",
                Detail = "The upload must be sent as application/octet-stream.",
                Status = StatusCodes.Status415UnsupportedMediaType
            });
        }

        // All metadata checks run before a single payload byte is read.
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

        var settings = uploadOptions.Value;
        var fileLimitBytes = FileLimitBytes(userId, settings);

        // Content-Length is the declared size: the idempotency fingerprint and
        // the mismatch check both come from it.
        long? itemSize = null;
        if (declaredLength is not null
            && long.TryParse(declaredLength, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLength))
        {
            itemSize = parsedLength;
        }

        if (itemSize is null)
        {
            return BadRequestProblem(
                "Content length required",
                "The upload must declare its exact size in the Content-Length header.");
        }

        if (itemSize <= 0)
        {
            return BadRequestProblem("Empty content", "The uploaded content must not be empty.");
        }

        if (itemSize > fileLimitBytes)
        {
            return FileTooLargeProblem(contentId, fileLimitBytes);
        }

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

        if (package.UserId != userId
            || !OpaqueToken.Matches(package.UploadTokenHash, uploadToken))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Forbidden",
                Detail = "Only the owner of the package can add content to it",
                Status = StatusCodes.Status403Forbidden
            });
        }

        var now = timeProvider.GetUtcNow();
        if (package.IsExpired(now))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Package expired",
                Detail = "This package has expired and is no longer available",
                Status = StatusCodes.Status410Gone
            });
        }

        activity?.SetTag("shareit.content.kind", KindOf(contentType));
        activity?.SetTag("shareit.content.size_bytes", itemSize);

        // Idempotent retry: a repeated upload with the same item id must not
        // create a duplicate row or fail on the primary key. Reusing the id
        // for different content is a conflict, not a silent success.
        var existing = await contentStorageHandler.GetContentAsync(contentId, cancellationToken);
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

            await DrainRequestBodyAsync(httpContext.Request, itemSize.Value, cancellationToken);

            activity?.SetTag("shareit.package.result", "existing");
            return TypedResults.Ok(ContentPackageOwnerResponse.FromPackage(package));
        }

        // The limit counts items, not bytes, and is checked after the
        // idempotency match so a retry never trips over it.
        if (package.ItemCount >= settings.MaxItemsPerPackage)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Too many items",
                Detail = $"This package can contain at most {settings.MaxItemsPerPackage} items.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var content = new Api.Models.Content
        {
            Id = contentId,
            PackageId = packageId,
            ContentType = contentType,
            CreatedAt = now,
            Size = itemSize.Value,
            UserId = userId,
            Meta = meta
        };

        await using var payload = new DeclaredLengthStream(httpContext.Request.Body, fileLimitBytes, itemSize.Value);

        try
        {
            await storageHandler.StoreContentAsync(content, payload, cancellationToken);
        }
        catch (ContentTooLargeException)
        {
            // The client sent more bytes than its limit allows, whatever it
            // declared. Storage compensation already removed any partial blob.
            return FileTooLargeProblem(contentId, fileLimitBytes);
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
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                return TypedResults.Empty;
            }

            return exceededLimit
                ? FileTooLargeProblem(contentId, fileLimitBytes)
                : BadRequestProblem(
                    "Content size mismatch",
                    "The uploaded content length does not match the declared size.");
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            return TypedResults.Empty;
        }

        metrics.ItemUploaded(contentType, itemSize.Value);
        activity?.SetTag("shareit.package.result", "uploaded");

        var updatedPackage = await contentStorageHandler.GetPackageShellAsync(packageId, cancellationToken);

        return TypedResults.Ok(ContentPackageOwnerResponse.FromPackage(updatedPackage ?? package));
    }

    /// <summary>
    /// Gets a package with all its content metadata and returns its access
    /// session. Item bytes are always fetched through that session; the open
    /// performs zero blob reads. A protected package requires a valid unlock
    /// proof before anything is read or an access is claimed.
    /// </summary>
    private static async Task<Results<Ok<ContentPackageDetailsResponse>, ProblemHttpResult>> GetPackageAsync(
        [FromRoute] Guid packageId,
        [FromHeader(Name = "X-Unlock-Proof")] string? unlockProof,
        [FromHeader(Name = "X-Access-Session")] string? accessSession,
        [FromServices] IContentStorageHandler storageHandler,
        [FromServices] IAccessSessionService accessSessionService,
        [FromServices] TimeProvider timeProvider,
        [FromServices] ShareItMetrics metrics,
        ClaimsPrincipal claimsPrincipal,
        HttpContext httpContext,
        ILogger<LogCategory> logger,
        CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.open");

        var userId = claimsPrincipal.GetUserId();
        activity?.SetTag("shareit.identity.type", userId is null ? "guest" : "user");

        // Shell first: no items, no names, no session, no blob reads.
        var package = await storageHandler.GetPackageShellAsync(packageId, cancellationToken);
        if (package is null)
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
        // reveals nothing. The header value is never logged. A short-code
        // package also counts the miss and burns itself after five attempts.
        if (package.UnlockVerifier is not null && !UnlockProof.Matches(package.UnlockVerifier, unlockProof))
        {
            logger.LogWarning("Failed unlock attempt for package {PackageId}", packageId);
            metrics.UnlockFailed();

            if (package.ShortCode is not null
                && await storageHandler.RegisterFailedUnlockAttemptAsync(
                    packageId, ShortCode.MaxFailedUnlockAttempts, now, cancellationToken))
            {
                metrics.ShortCodeBurned();
                logger.LogWarning(
                    "Short-code package {PackageId} burned after {Attempts} failed unlock attempts",
                    packageId, ShortCode.MaxFailedUnlockAttempts);
            }

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
                userId,
                cancellationToken);
            activity?.SetTag("shareit.access.session_reused", existingSession is not null);
        }

        // An exhausted package with no valid session cannot succeed below;
        // answer 410 here before claiming.
        if (existingSession is null && withContent.IsAccessLimitReached)
        {
            return AccessLimitReachedProblem();
        }

        var orderedContents = withContent.Contents
            .OrderBy(content => content.CreatedAt)
            .ThenBy(content => content.Id.ToString("N"), StringComparer.Ordinal);

        // C# 15 collection expression argument: pre-size the list because the
        // number of items is already known.
        List<ContentItemInPackage> items = [with(capacity: withContent.Contents.Count)];
        foreach (var content in orderedContents)
        {
            // Nothing is minted or read here: the package's access session
            // authorizes a lazy download when the recipient opens the item.
            items.Add(new ContentItemInPackage
            {
                Id = content.Id,
                Kind = KindOf(content.ContentType),
                Size = content.Size,
                Meta = content.Meta
            });
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
                userId,
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

        var userId = claimsPrincipal.GetUserId();
        activity?.SetTag("shareit.identity.type", userId is null ? "guest" : "user");

        // Read-only validation: downloads never consume the session.
        var session = await accessSessionService.ValidateAsync(
            accessSession,
            packageId,
            userId,
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
                    Type = ProblemTypes.PackageGone,
                    Title = "Package not found",
                    Detail = $"Package with ID {packageId} was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            if (packageShell.IsExpired(timeProvider.GetUtcNow()))
            {
                return TypedResults.Problem(new ProblemDetails
                {
                    Type = ProblemTypes.PackageGone,
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
                Type = ProblemTypes.PackageGone,
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
            stream = await blobStorageHandler.DownloadAsync(content.Url ?? string.Empty, cancellationToken);
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
            AuthenticatedMaxAccessCount = settings.Authenticated.MaxAccessCount,
            MaxItemsPerPackage = settings.MaxItemsPerPackage
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

    private static string KindOf(ContentType contentType)
        => contentType == ContentType.Text ? "text" : "file";

    /// <summary>
    /// Resolves the per-file upload limit for the given identity.
    /// </summary>
    private static long FileLimitBytes(Guid? userId, UploadSettings settings)
    {
        var identityLimits = userId is null ? settings.Anonymous : settings.Authenticated;
        return identityLimits.FileLimitMb * 1024L * 1024L;
    }

    /// <summary>
    /// Enforces the per-identity package lifetime limits. Every package must
    /// set a lifetime: abandoned uploads always expire and are swept by
    /// cleanup, without any draft/ready state machine.
    /// </summary>
    private static ProblemHttpResult? ValidateLifetime(Guid? userId, UploadSettings settings, int timeToLiveHours)
    {
        if (timeToLiveHours <= 0)
        {
            return BadRequestProblem(
                "Invalid lifetime",
                "TimeToLiveHours must be a positive number of hours.");
        }

        var identityLimits = userId is null ? settings.Anonymous : settings.Authenticated;
        if (timeToLiveHours > identityLimits.MaxLifetimeHours)
        {
            return BadRequestProblem(
                "Lifetime too long",
                $"Packages for this identity may live for at most {identityLimits.MaxLifetimeHours} hours.");
        }

        return null;
    }

    private static ProblemHttpResult? ValidateMaxAccessCount(
        Guid? userId, UploadSettings settings, int? maxAccessCount)
    {
        if (maxAccessCount is { } count)
        {
            if (count <= 0)
            {
                return BadRequestProblem(
                    "Invalid access limit",
                    "MaxAccessCount must be a positive number.");
            }

            var identityLimits = userId is null ? settings.Anonymous : settings.Authenticated;
            if (count > identityLimits.MaxAccessCount)
            {
                return BadRequestProblem(
                    "Access limit too high",
                    $"Packages for this identity allow at most {identityLimits.MaxAccessCount} accesses.");
            }

            return null;
        }

        if (userId is null)
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
