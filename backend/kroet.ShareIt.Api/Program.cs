using Asp.Versioning;
using kroet.Permissions;
using kroet.ShareIt.Api;
using kroet.ShareIt.Api.Authentication;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content;
using kroet.ShareIt.Api.Services.Cleanup;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// The initial "bootstrap" logger is able to log errors during start-up. It's completely replaced by the
// logger configured in `AddSerilog()` below, once configuration and dependency-injection have both been
// set up successfully.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var configuration = builder.Configuration;
    
    builder.Services.AddSerilog((_, lc) => { lc.ReadFrom.Configuration(configuration); });
    
    builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
    {
        options.SerializerOptions.PropertyNameCaseInsensitive = true;
        options.SerializerOptions.PropertyNamingPolicy = null;
    });
    
    builder.Services.AddCors();

    // Upload size limits (also enforced per-file in the content endpoints)
    builder.Services.AddOptions<UploadSettings>()
        .BindConfiguration(UploadSettings.SectionName)
        .ValidateWithFluentValidator<UploadSettings, UploadSettingsValidator>()
        .ValidateOnStart();

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
        builder.Services.AddAuthentication().AddJwtBearer(options =>
        {
            options.Authority = authAuthority;
            // The SPA uses authorization-code + PKCE; access tokens carry the
            // client id as audience. Audience validation is skipped until an
            // explicit API audience is configured.
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false
            };
        });
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
    
    app.UseSerilogRequestLogging();
    
    app.UseCors(options =>
    {
        options.AllowAnyHeader();
        options.AllowAnyMethod();
        options.AllowAnyOrigin();
    });

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