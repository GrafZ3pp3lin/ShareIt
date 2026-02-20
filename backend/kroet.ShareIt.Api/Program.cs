using System.Data;
using Asp.Versioning;
using kroet.Permissions;
using kroet.ShareIt.Api.Content;
using kroet.ShareIt.Api.Content.Configuration;
using kroet.ShareIt.Api.Content.Extensions;
using kroet.ShareIt.Api.Content.Services;
using kroet.ShareIt.Api.Content.Storage;
using kroet.ShareIt.Api.Content.Storage.Blobs;
using kroet.ShareIt.Api.Content.Storage.Package;
using MongoDB.Driver;
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
    
    // Blob Storage configuration
    builder.Services.AddBlobStorage(configuration);
    
    // Register content storage handler
    builder.Services.AddContentStorage(configuration);
    
    // Main storage handler
    builder.Services.AddSingleton<IStorageHandler, StorageHandler>();

    // Download settings and token service
    builder.Services.AddDownloadTokenService();

    // Register System TimeProvider
    builder.Services.AddSingleton(TimeProvider.System);

    // Background cleanup service
    builder.Services.AddCleanupService();
    
    builder.Services.AddAuthentication();
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
    
    // Map content management endpoints
    v1ApiEndpoints.MapGroup("/v1/content")
        .MapContentEndpoints();

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