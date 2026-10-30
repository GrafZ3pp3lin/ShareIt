using kroet.ShareIt.AuthTwin.Realm;

namespace kroet.ShareIt.AuthTwin.Hosting;

internal sealed class TwinUrls(TwinPublicUrl publicUrl, RealmModel realm)
{
    public string PublicUrl => publicUrl.Value;

    public string Issuer => $"{PublicUrl}/realms/{realm.Name}";

    public string OidcEndpoint(string name) => $"{Issuer}/protocol/openid-connect/{name}";
}
