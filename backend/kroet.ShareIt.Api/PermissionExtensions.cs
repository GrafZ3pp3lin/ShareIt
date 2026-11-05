using kroet.Permission.AspNetCore;

namespace kroet.ShareIt.Api;

public static class PermissionExtensions
{
    extension(IHost host)
    {
        public IHost MapPermissions()
        {
            host.MapPermission(manager =>
            {
                manager.RegisterPermission("Upload.File", ForEveryone);
                manager.RegisterPermission("Download.File", ForEveryone);
                
                manager.RegisterPermission("Create.Package", ForEveryone);
                manager.RegisterPermission("Read.Package", ForEveryone);
                manager.RegisterPermission("Read.Limits", ForEveryone);
                manager.RegisterPermission("List.Packages", ForAuthenticatedUsers);
                manager.RegisterPermission("Delete.Package", ForAuthenticatedUsers);
            });

            return host;
        }
    }

    private static bool ForEveryone(System.Security.Claims.ClaimsPrincipal user)
    {
        return true;
    }
    
    private static bool ForAuthenticatedUsers(System.Security.Claims.ClaimsPrincipal user)
    {
        var userIsAnonymous = user.Identity is null || !user.Identities.Any(i => i.IsAuthenticated);
        
        return !userIsAnonymous;
    }

    private static bool ForAdmins(System.Security.Claims.ClaimsPrincipal user)
    {
        return user.HasClaim(c => c is { Type: "role", Value: "Admin" });
    }
}