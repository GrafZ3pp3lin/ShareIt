using System.Net;

namespace kroet.ShareIt.AuthTwin.Hosting;

internal static class TwinHostingExtensions
{
    public static IServiceCollection AddTwinHosting(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TwinPublicUrl>();
        return services;
    }

    public static IApplicationBuilder UseTwinRequestLog(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AuthTwin");
            logger.LogInformation(
                "{Method} {Path}{Query}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString);
            await next(context);
        });

    /// <summary>The control plane has no authentication, so only loopback callers are served.</summary>
    public static RouteGroupBuilder MapTwinControlPlane(this IEndpointRouteBuilder app) =>
        app.MapGroup("/control").AddEndpointFilter(async (invocation, next) =>
        {
            var remote = invocation.HttpContext.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote)) return Results.NotFound();
            return await next(invocation);
        });
}
