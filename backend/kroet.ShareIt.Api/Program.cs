using System.Threading.RateLimiting;
using Asp.Versioning;
using kroet.Permissions;
using kroet.ShareIt.Api;
using kroet.ShareIt.Api.Authentication;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content;
using kroet.ShareIt.Api.Services;
using kroet.ShareIt.Api.Services.Cleanup;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// The initial "bootstrap" logger is able to log errors during start-up. It's completely replaced by the
// logger configured by `AddObservability()` below, once configuration and dependency-injection
// have both been set up successfully.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var configuration = builder.Configuration;
    
    builder.AddObservability();
    
    builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
    {
        options.SerializerOptions.PropertyNameCaseInsensitive = true;
        options.SerializerOptions.PropertyNamingPolicy = null;
    });
    
    builder.Services.AddCors();

    // Map unhandled exceptions to RFC 7807 ProblemDetails (HTTP 500)
    // instead of letting them escape as bare server responses.
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // The API runs behind Traefik, which terminates TLS. Without this, Kestrel
    // sees http as the request scheme/host — which breaks DPoP's htu
    // comparison (RFC 9449: the proof binds scheme, authority and path).
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        // Traefik is the only entrypoint (docker/private network), so trust it
        // regardless of its address.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Upload size limits (also enforced per-file in the content endpoints)
    builder.Services.AddOptions<UploadSettings>()
        .BindConfiguration(UploadSettings.SectionName)
        .ValidateWithFluentValidator<UploadSettings, UploadSettingsValidator>()
        .ValidateOnStart();

    // Rate limiting for package reads: partitioned on (package id, remote IP),
    // so unlocking counts towards the same budget as failed attempts and one
    // NAT cannot lock everyone out.
    builder.Services.AddOptions<RateLimitSettings>()
        .BindConfiguration(RateLimitSettings.SectionName)
        .ValidateWithFluentValidator<RateLimitSettings, RateLimitSettingsValidator>()
        .ValidateOnStart();

    var rateLimitSettings = configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>()
                            ?? new RateLimitSettings();
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(RateLimitPolicies.PackageRead, httpContext =>
        {
            var packageId = httpContext.GetRouteValue("packageId")?.ToString() ?? "unknown";
            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                $"{packageId}|{remoteIp}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.PackageReadPermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimitSettings.PackageReadWindowMinutes),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.RequestServices.GetRequiredService<ShareItMetrics>().RateLimited();
            await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Too many requests",
                Detail = "Too many attempts for this link. Please try again later.",
                Status = StatusCodes.Status429TooManyRequests
            }, cancellationToken);
        };
    });

    var uploadSettings = configuration.GetSection(UploadSettings.SectionName).Get<UploadSettings>() ?? new UploadSettings();
    var maxRequestBodySizeBytes = uploadSettings.MaxRequestBodySizeMb * 1024L * 1024L;
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = maxRequestBodySizeBytes;
    });
    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = maxRequestBodySizeBytes;
        options.ValueLengthLimit = 1024 * 1024;
    });

    // Blob Storage configuration
    builder.Services.AddBlobStorage(configuration);
    
    // Register content storage handler
    builder.Services.AddContentStorage(configuration);
    
    // Register download token storage handler
    builder.Services.AddDownloadTokenStorage(configuration);
    
    // Main storage handler
    builder.Services.AddSingleton<IStorageHandler, StorageHandler>();

    // Register System TimeProvider
    builder.Services.AddSingleton(TimeProvider.System);

    // Background cleanup service
    builder.Services.AddCleanupService();
    
    // Authentication:
    // - JWT bearer validation is only enabled when Auth:Authority is configured,
    //   so auth stays fully optional per environment.
    // - A plain challenge/forbid scheme answers denied anonymous requests with
    //   401/403 instead of throwing (no interactive sign-in exists server-side).
    var authAuthority = configuration["Auth:Authority"];
    builder.Services.AddAuthentication(options =>
        {
            options.DefaultChallengeScheme = ApiChallengeHandler.SchemeName;
            if (!string.IsNullOrEmpty(authAuthority))
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }
        })
        .AddScheme<AuthenticationSchemeOptions, ApiChallengeHandler>(ApiChallengeHandler.SchemeName, _ => { });

    if (!string.IsNullOrEmpty(authAuthority))
    {
        var authAudience = configuration["Auth:Audience"];
        // Local Keycloak twins run on plain http; every real deployment keeps the default.
        var requireHttpsMetadata = configuration.GetValue("Auth:RequireHttpsMetadata", true);
        builder.Services.AddOptions<DpopSettings>().BindConfiguration(DpopSettings.SectionName);
        builder.Services.AddSingleton<DpopProofValidator>();
        builder.Services.AddAuthentication().AddJwtBearer(options =>
        {
            options.Authority = authAuthority;
            options.Audience = authAudience;
            options.RequireHttpsMetadata = requireHttpsMetadata;
            // Keep JWT claim names (sub, email, ...) instead of ASP.NET's legacy
            // SOAP mappings: the API reads "sub" directly.
            options.MapInboundClaims = false;
            // The SPA uses authorization-code + PKCE; access tokens carry the
            // client id as audience. Audience validation is enforced as soon as
            // an explicit API audience is configured (Auth:Audience).
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = !string.IsNullOrEmpty(authAudience)
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // DPoP-bound tokens use the DPoP authorization scheme instead
                    // of Bearer (RFC 9449 §7.1); accept both.
                    var authorization = context.Request.Headers.Authorization.ToString();
                    if (authorization.StartsWith("DPoP ", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Token = authorization["DPoP ".Length..].Trim();
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    // DPoP proof validation (RFC 9449 §7.2): tokens bound to a
                    // client key (cnf.jkt) require a fresh, single-use proof that
                    // covers the token, method and URI. Plain bearer tokens pass
                    // while Auth:Dpop:Required is false.
                    var authorization = context.Request.Headers.Authorization.ToString();
                    var separator = authorization.IndexOf(' ');
                    if (separator <= 0)
                    {
                        context.Fail("Malformed Authorization header.");
                        return;
                    }

                    var validator = context.HttpContext.RequestServices.GetRequiredService<DpopProofValidator>();
                    var result = await validator.ValidateAsync(
                        context.Request.Headers["DPoP"].ToString(),
                        authorization[(separator + 1)..].Trim(),
                        context.Principal?.FindFirst("cnf")?.Value,
                        context.Request);
                    // C# 15 union: exactly one of the two cases is present.
                    if (result.Value is DpopFailure failure)
                    {
                        context.HttpContext.Items[DpopProofValidator.DpopErrorItemKey] = failure.ErrorCode;
                        context.Fail(failure.ErrorDescription);
                    }
                },
                OnChallenge = context =>
                {
                    // With Auth:Dpop:Required, DPoP is the only accepted scheme,
                    // so challenges advertise DPoP (RFC 9449 §7.1) instead of the
                    // JWT bearer handler's default Bearer challenge, whose error
                    // details would otherwise be attached to the wrong scheme.
                    var dpopSettings = context.HttpContext.RequestServices
                        .GetRequiredService<IOptions<DpopSettings>>().Value;
                    if (!dpopSettings.Required)
                    {
                        return Task.CompletedTask;
                    }

                    var errorCode = context.HttpContext.Items.TryGetValue(DpopProofValidator.DpopErrorItemKey, out var value)
                        ? value as string ?? "invalid_dpop_proof"
                        : "invalid_token";
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate =
                        DpopProofValidator.BuildChallenge(errorCode, context.AuthenticateFailure?.Message);
                    context.HandleResponse();
                    return Task.CompletedTask;
                }
            };
        });

        if (string.IsNullOrEmpty(authAudience))
        {
            Log.Warning(
                "Auth:Authority is configured without Auth:Audience — audience validation is disabled. " +
                "Tokens issued to any client of {Authority} are accepted; set Auth:Audience to restrict them to this API.",
                authAuthority);
        }
    }

    builder.Services.AddAuthorization();
    builder.Services.AddPermissionAuthorization();

    builder.Services.AddApiVersioning(options =>
    {
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.ApiVersionReader = new QueryStringApiVersionReader();
    });
        
    builder.Services.AddOpenApi();

    var app = builder.Build();

    var metrics = app.Services.GetRequiredService<ShareItMetrics>();
    metrics.SetBuildInfo(
        ObservabilityExtensions.ResolveServiceVersion(),
        app.Environment.EnvironmentName);

    app.UseForwardedHeaders();

    app.UseSerilogRequestLogging();
    
    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    app.UseCors(policy =>
    {
        policy.AllowAnyHeader();
        policy.AllowAnyMethod();
        if (allowedOrigins.Length == 0)
        {
            // No origins configured — stay permissive, but call it out in production.
            if (app.Environment.IsProduction())
            {
                Log.Warning("No CORS origins configured (Cors:AllowedOrigins) — falling back to AllowAnyOrigin.");
            }

            policy.AllowAnyOrigin();
        }
        else
        {
            policy.SetIsOriginAllowedToAllowWildcardSubdomains()
                  .WithOrigins(allowedOrigins);
        }
    });

    // Must come after UseCors so problem responses still carry CORS headers.
    app.UseExceptionHandler();

    app.UseRateLimiter();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    
    var versionSet = app.NewApiVersionSet()
        .HasApiVersion(new ApiVersion(1, 0))
        .ReportApiVersions()
        .Build();
    
    var v1ApiEndpoints = app.MapGroup("/api")
        .WithApiVersionSet(versionSet)
        .MapToApiVersion(new ApiVersion(1, 0));

    var v1ContentEndpoints = v1ApiEndpoints.MapGroup("/v1/content");

    // Map content endpoints
    v1ContentEndpoints.MapContentEndpoints();

    // Map content management endpoints (authenticated users only)
    v1ContentEndpoints.MapContentManagementEndpoints();

    // Register permission grants
    app.MapPermissions();

    await app.InitializePackageStorageAsync();
    await app.InitializeBlobStorageAsync();

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "An unhandled exception occurred during bootstrapping");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

// Declared public so the integration test project can boot the API
// with WebApplicationFactory<Program>.
public partial class Program { }