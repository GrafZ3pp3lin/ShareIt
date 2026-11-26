using System.Threading.RateLimiting;
using Asp.Versioning;
using kroet.ShareIt.Api;
using kroet.ShareIt.Api.Authentication;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content;
using kroet.ShareIt.Api.Services;
using kroet.ShareIt.Api.Services.Cleanup;
using kroet.ShareIt.Api.Services.Health;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// Docker healthcheck probe mode for the chiseled runtime image (no shell,
// no HTTP client): `dotnet kroet.ShareIt.Api.dll --healthcheck [url]`
// performs a single GET against the readiness endpoint and exits with its
// HTTP result, without starting the server.
if (args.FirstOrDefault() == "--healthcheck")
{
    return await HealthCheckProbe.RunAsync(args.Skip(1).FirstOrDefault());
}

WebApplication? app = null;

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
    // so unlocking counts towards the same budget as failed attempts.
    // Downloads are limited per remote IP: the package id is client-supplied
    // and would create unbounded limiter partitions.
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
            var remoteIp = RemoteIpKey.From(httpContext.Connection.RemoteIpAddress);

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
        options.AddPolicy(RateLimitPolicies.ShortCodeLookup, httpContext =>
        {
            var remoteIp = RemoteIpKey.From(httpContext.Connection.RemoteIpAddress);

            return RateLimitPartition.GetFixedWindowLimiter(
                remoteIp,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.ShortCodeLookupPermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimitSettings.ShortCodeLookupWindowMinutes),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
        options.AddPolicy(RateLimitPolicies.Download, httpContext =>
        {
            var remoteIp = RemoteIpKey.From(httpContext.Connection.RemoteIpAddress);

            return RateLimitPartition.GetFixedWindowLimiter(
                remoteIp,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.DownloadPermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimitSettings.DownloadWindowMinutes),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
        options.AddPolicy(RateLimitPolicies.Upload, httpContext =>
        {
            var remoteIp = RemoteIpKey.From(httpContext.Connection.RemoteIpAddress);

            return RateLimitPartition.GetFixedWindowLimiter(
                remoteIp,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.UploadPermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimitSettings.UploadWindowMinutes),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";

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
                Detail = "Too many attempts. Please try again later.",
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

    // Uploads are read from Request.Body as raw binary PUTs, so no form
    // buffering options apply: the per-identity file limit is enforced while
    // streaming, and Kestrel's body limit stays the outer bound.

    // Blob Storage configuration
    builder.Services.AddBlobStorage(configuration);

    // Register content storage handler
    builder.Services.AddContentStorage(configuration);

    // Code-mode packages get a server-generated locator; regular links have none.
    builder.Services.AddSingleton<IShortCodeGenerator, ShortCodeGenerator>();

    // Register access session storage handler
    builder.Services.AddAccessSessionStorage();

    // Main storage handler
    builder.Services.AddSingleton<IStorageHandler, StorageHandler>();

    // Liveness/readiness health checks for orchestrators and load balancers
    builder.Services.AddShareItHealthChecks();

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
    var authAudience = configuration["Auth:Audience"];
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
                    var separator = authorization.IndexOf(' ', StringComparison.Ordinal);
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
    }

    builder.Services.AddAuthorization();

    builder.Services.AddApiVersioning(options =>
    {
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ApiVersionReader = new QueryStringApiVersionReader();
    });

    builder.Services.AddOpenApi(options =>
    {
        // The raw binary upload body is read directly from Request.Body, so no
        // parameter binding describes it. Document the request body here so
        // the OpenAPI spec (and the generated client) carries it.
        options.AddOperationTransformer((operation, context, cancellationToken) =>
        {
            if (operation is not null && string.Equals(operation.OperationId, "UploadContent", StringComparison.Ordinal))
            {
                operation.RequestBody = new Microsoft.OpenApi.OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, Microsoft.OpenApi.IOpenApiMediaType>
                    {
                        ["application/octet-stream"] = new Microsoft.OpenApi.OpenApiMediaType
                        {
                            Schema = new Microsoft.OpenApi.OpenApiSchema
                            {
                                Type = Microsoft.OpenApi.JsonSchemaType.String,
                                Format = "binary"
                            }
                        }
                    }
                };
            }

            return Task.CompletedTask;
        });

    });

    app = builder.Build();

    if (!string.IsNullOrEmpty(authAuthority) && string.IsNullOrEmpty(authAudience))
    {
        app.Logger.LogWarning(
            "Auth:Authority is configured without Auth:Audience — audience validation is disabled. " +
            "Tokens issued to any client of {Authority} are accepted; set Auth:Audience to restrict them to this API.",
            authAuthority);
    }

    var metrics = app.Services.GetRequiredService<ShareItMetrics>();
    metrics.SetBuildInfo(
        ObservabilityExtensions.ResolveServiceVersion(),
        app.Environment.EnvironmentName);

    app.UseForwardedHeaders();

    app.UseRequestLogging();

    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    app.UseCors(policy =>
    {
        policy.AllowAnyHeader();
        policy.AllowAnyMethod();
        policy.WithExposedHeaders("Retry-After");
        if (allowedOrigins.Length == 0)
        {
            // Development stays permissive for local tooling. Production only
            // serves same-origin requests until explicit origins are configured,
            // so a missing setting cannot silently expose the API to any site.
            if (app.Environment.IsProduction())
            {
                app.Logger.LogWarning(
                    "No CORS origins configured (Cors:AllowedOrigins) — cross-origin requests are disabled.");
            }
            else
            {
                policy.AllowAnyOrigin();
            }
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

    // Liveness/readiness probes, outside the versioned API surface.
    app.MapHealthEndpoints();

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

    await app.InitializePackageStorageAsync();
    await app.InitializeBlobStorageAsync();

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    if (app is null)
    {
        await Console.Error.WriteLineAsync($"An unhandled exception occurred during bootstrapping: {ex}");
    }
    else
    {
        app.Logger.LogCritical(ex, "An unhandled exception occurred during bootstrapping");
        await app.DisposeAsync();
    }

    return 1;
}
