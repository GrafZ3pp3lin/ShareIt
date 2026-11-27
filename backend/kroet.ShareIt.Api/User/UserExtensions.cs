using System.Security.Claims;

namespace kroet.ShareIt.Api.User;

public static class UserExtensions
{
    extension(ClaimsPrincipal claimsPrincipal)
    {
        /// <summary>
        /// Returns the verified OIDC subject as a user id, or null for an
        /// unauthenticated caller. Guests have no identity of their own.
        /// </summary>
        public Guid? GetUserId()
        {
            var userIdClaim = claimsPrincipal.FindFirst("sub");
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return null;
            }

            return userId;
        }

        /// <summary>
        /// Resolves an authenticated user only, for endpoints that require
        /// authorization. Guests have no user id here.
        /// </summary>
        public bool TryGetUserId(out Guid userId)
        {
            var resolved = claimsPrincipal.GetUserId();
            userId = resolved ?? Guid.Empty;
            return resolved.HasValue;
        }
    }
}
