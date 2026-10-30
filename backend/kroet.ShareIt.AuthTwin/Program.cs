namespace kroet.ShareIt.AuthTwin;

// No top-level statements: their generated global Program would clash with the API's in test runs.
internal static class Program
{
    public static Task Main(string[] args)
    {
        var app = AuthTwinHost.Build(args);
        // Loopback only: the control plane has no authentication.
        if (string.IsNullOrEmpty(app.Configuration["urls"])) app.Urls.Add("http://localhost:8190");
        return app.RunAsync();
    }
}
