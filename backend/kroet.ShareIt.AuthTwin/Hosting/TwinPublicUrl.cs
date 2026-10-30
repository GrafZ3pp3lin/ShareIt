using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace kroet.ShareIt.AuthTwin.Hosting;

/// <summary>Like Keycloak's hostname setting: the URL the twin is reachable at.</summary>
internal sealed class TwinPublicUrl(IConfiguration configuration, IServer server)
{
    public string Value
    {
        get
        {
            var configured = configuration["Twin:PublicUrl"];
            if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');

            var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                          ?? throw new InvalidOperationException("The twin's server address is not available yet.");
            return address
                .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal)
                .Replace("://[::]", "://localhost", StringComparison.Ordinal)
                .TrimEnd('/');
        }
    }
}
