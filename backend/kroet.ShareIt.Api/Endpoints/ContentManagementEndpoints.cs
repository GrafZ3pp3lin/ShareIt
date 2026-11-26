using System.Diagnostics;
using System.Security.Claims;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.User;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace kroet.ShareIt.Api.Content;

public static class ContentManagementEndpoints
{
    /// <summary>
    /// Maps all content management endpoints to the route group.
    /// </summary>
    public static RouteGroupBuilder MapContentManagementEndpoints(this RouteGroupBuilder group)
    {
        var packageGroup = group.MapGroup("/packages");

        // The owner's links list: authenticated only, no names server-side.
        packageGroup
            .MapGet("", ListPackagesAsync)
            .RequireAuthorization()
            .Produces<ContentPackageListResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .WithName("ListPackages")
            .WithTags("Content", "Package");

        // Package endpoints
        packageGroup
            .MapDelete("/{packageId:guid}", DeletePackageAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .WithName("DeletePackage")
            .WithTags("Content", "Package");

        return packageGroup;
    }

    /// <summary>
    /// Lists the authenticated owner's packages, newest first. Rows carry no
    /// names — only timestamps, counters and sizes.
    /// </summary>
    private static async Task<Results<Ok<ContentPackageListResponse>, ProblemHttpResult>> ListPackagesAsync(
        [FromQuery] int? take,
        [FromQuery] string? cursor,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] TimeProvider timeProvider,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        if (!claimsPrincipal.TryGetUserIdentity(out var userIdentity))
        {
            return AuthenticationRequiredProblem();
        }

        var pageSize = Math.Clamp(take ?? 50, 1, 100);
        var page = await contentStorageHandler.ListPackagesAsync(userIdentity.Id, cursor, pageSize, cancellationToken);
        var now = timeProvider.GetUtcNow();

        return TypedResults.Ok(new ContentPackageListResponse
        {
            Items = [.. page.Items.Select(summary => ContentPackageListItem.FromSummary(summary, now))],
            NextCursor = page.NextCursor
        });
    }

    /// <summary>
    /// Deletes a package owned by the authenticated user, queueing its blobs
    /// for the cleanup service.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeletePackageAsync(
        [FromRoute] Guid packageId,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] IStorageHandler storageHandler,
        [FromServices] ShareItMetrics metrics,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        using var activity = ShareItActivitySource.Instance.StartActivity("package.delete");

        if (!claimsPrincipal.TryGetUserIdentity(out var userIdentity))
        {
            return AuthenticationRequiredProblem();
        }

        var package = await contentStorageHandler.GetPackageAsync(packageId, false, cancellationToken);
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
                Detail = "Only the owner of the package can delete it",
                Status = StatusCodes.Status403Forbidden
            });
        }

        var success = await storageHandler.DeletePackageAsync(packageId, cancellationToken);

        if (!success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "delete failed");
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Failed to delete package",
                Detail = $"An error occurred while deleting package with ID {packageId}",
                Status = StatusCodes.Status500InternalServerError
            });
        }

        metrics.PackageDeleted("owner");
        activity?.SetTag("shareit.package.result", "deleted");
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult AuthenticationRequiredProblem()
        => TypedResults.Problem(new ProblemDetails
        {
            Title = "Authentication required",
            Detail = "Only authenticated users can manage their packages",
            Status = StatusCodes.Status401Unauthorized
        });
}
