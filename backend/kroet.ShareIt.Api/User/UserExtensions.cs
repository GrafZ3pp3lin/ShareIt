using System.Security.Claims;

namespace kroet.ShareIt.Api.User;

public static class UserExtensions
{
    extension(ClaimsPrincipal claimsPrincipal)
    {
        private AuthenticatedUserIdentity? ToOptionalUserIdentity()
        {
            var userIdClaim = claimsPrincipal.FindFirst("sub");
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return null;
            }

            return new AuthenticatedUserIdentity
            {
                Id = userId,
            };
        }

        public bool TryGetUserIdentity(Guid? guestId, out UserIdentity userIdentity)
        {
            var identity = claimsPrincipal.ToOptionalUserIdentity();
            if (identity != null)
            {
                userIdentity = identity;
                return true;
            }

            if (guestId.HasValue)
            {
                userIdentity = new GuestUserIdentity
                {
                    Id = guestId.Value
                };
                return true;
            }

            userIdentity = null!;
            return false;
        }

        /// <summary>
        /// Resolves the authenticated user only, for endpoints that require
        /// authorization. Guests have no identity here.
        /// </summary>
        public bool TryGetUserIdentity(out UserIdentity userIdentity)
        {
            var identity = claimsPrincipal.ToOptionalUserIdentity();
            if (identity is null)
            {
                userIdentity = null!;
                return false;
            }

            userIdentity = identity;
            return true;
        }
    }
}
