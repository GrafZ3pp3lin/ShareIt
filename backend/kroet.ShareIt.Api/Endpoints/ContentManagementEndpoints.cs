using System.Security.Claims;
using kroet.Permission.AspNetCore;
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

        // Package endpoints
        packageGroup
            .MapDelete("/{packageId:guid}", DeletePackageAsync)
            .RequirePermission("Delete.Package")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .WithName("DeletePackage")
            .WithTags("Content", "Package");

        return packageGroup;
    }

    /// <summary>
    /// Deletes a package and optionally its content.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeletePackageAsync(
        [FromRoute] Guid packageId,
        [FromHeader(Name = "X-Guest-ID")] Guid? guestId,
        [FromServices] IContentStorageHandler contentStorageHandler,
        [FromServices] IStorageHandler storageHandler,
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
                Title = "Forbidden", Detail = "Only the owner of the package can delete it", Status = StatusCodes.Status403Forbidden
            });
        }

        var success = await contentStorageHandler.DeletePackageAsync(packageId, cancellationToken);

        if (!success)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Failed to delete package",
                Detail = $"An error occurred while deleting package with ID {packageId}",
                Status = StatusCodes.Status500InternalServerError
            });
        }

        return TypedResults.NoContent();
    }
}