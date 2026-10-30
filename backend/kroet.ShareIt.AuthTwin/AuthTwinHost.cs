using kroet.ShareIt.AuthTwin.Control;
using kroet.ShareIt.AuthTwin.Hosting;
using kroet.ShareIt.AuthTwin.Oidc;
using kroet.ShareIt.AuthTwin.Realm;
using kroet.ShareIt.AuthTwin.Store;

namespace kroet.ShareIt.AuthTwin;

/// <summary>Starts the twin in-process, for tests. <c>dotnet run</c> uses <see cref="Build"/> directly.</summary>
public sealed class AuthTwinHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private AuthTwinHost(WebApplication app)
    {
        _app = app;
        BaseUrl = new Uri(app.Urls.First().TrimEnd('/') + "/");
        Issuer = app.Services.GetRequiredService<TwinUrls>().Issuer;
    }

    /// <summary>Where the twin listens. Differs from the issuer's origin when <c>Twin:PublicUrl</c> is set.</summary>
    public Uri BaseUrl { get; }

    public string Issuer { get; }

    /// <param name="settings">Configuration such as <c>Twin:PublicUrl</c> or <c>Twin:RealmFile</c>.</param>
    public static Task<AuthTwinHost> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        CancellationToken cancellationToken = default) =>
        StartAsync(settings, null, cancellationToken);

    /// <param name="configureServices">Replaces services before the twin starts, e.g. its <see cref="TimeProvider"/>.</param>
    internal static async Task<AuthTwinHost> StartAsync(
        IReadOnlyDictionary<string, string?>? settings,
        Action<IServiceCollection>? configureServices,
        CancellationToken cancellationToken = default)
    {
        var app = Build([], settings, configureServices);
        app.Urls.Add("http://127.0.0.1:0");
        try
        {
            await app.StartAsync(cancellationToken);
            return new AuthTwinHost(app);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    internal static WebApplication Build(
        string[] args,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(AuthTwinHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });
        if (settings is not null) builder.Configuration.AddInMemoryCollection(settings);

        var realmPath = builder.Configuration["Twin:RealmFile"];
        var realmFile = string.IsNullOrWhiteSpace(realmPath)
            ? Path.Combine(AppContext.BaseDirectory, "realm-shareit.json")
            : Path.GetFullPath(realmPath, AppContext.BaseDirectory);
        var realm = RealmFile.Load(realmFile);

        builder.Services.AddTwinHosting();
        builder.Services.AddSingleton(realm);
        builder.Services.AddSingleton<SigningKey>();
        builder.Services.AddSingleton<TwinUrls>();
        builder.Services.AddSingleton<UserStore>();
        builder.Services.AddSingleton<AuthStore>();
        builder.Services.AddSingleton<TokenFactory>();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseTwinRequestLog();
        app.MapDiscovery();
        app.MapAuthorization();
        app.MapToken();
        app.MapSessions();
        app.MapControlPlane();
        return app;
    }
}
