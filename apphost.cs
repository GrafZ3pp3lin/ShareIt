#:package Aspire.Hosting.AppHost@17.0.0
#:package Aspire.Hosting.Dotnet@17.0.0
#:package Aspire.Hosting.JavaScript@17.0.0
#:package Aspire.Hosting.PostgreSQL@17.0.0

#:property TargetFramework=net11.0
#:property UserSecretsId=6a3c8f3f-5e7e-4d2b-9c4a-1f8e2b7d6c51

// Ports are fixed because frontend/public/env.js and the API's launch profile point at them.
const int ApiPort = 5155;
const int AuthTwinPort = 8190;
const int FrontendPort = 3001;

// Development-only credentials for the local containers.
const string PostgresPassword = "shareit-dev-password";
const string RustFsAccessKey = "shareit-dev-access-key";
const string RustFsSecretKey = "shareit-dev-secret-key";

var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", PostgresPassword, secret: true);
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("19-alpine")
    .WithPassword(postgresPassword)
    // Bump this name when the schema changes incompatibly; there are no migrations.
    .WithDataVolume("shareit-postgres-data-v2");
var database = postgres.AddDatabase("shareit");

// Mirrors the RustFS container used by the integration tests.
var rustFs = builder.AddContainer("rustfs", "rustfs/rustfs", "1.0.6")
    .WithEnvironment("RUSTFS_ACCESS_KEY", RustFsAccessKey)
    .WithEnvironment("RUSTFS_SECRET_KEY", RustFsSecretKey)
    .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "false")
    .WithEnvironment("RUSTFS_CHECK_UPDATE", "false")
    .WithEnvironment("RUSTFS_OBS_LOGGER_LEVEL", "error")
    .WithArgs("/data")
    .WithVolume("shareit-rustfs-data", "/data")
    .WithHttpEndpoint(targetPort: 9000, name: "http")
    .WithHttpHealthCheck("/health");

var authTwin = builder.AddDotnetProject("auth-twin", "backend/kroet.ShareIt.AuthTwin/kroet.ShareIt.AuthTwin.csproj")
    .WithHttpEndpoint(port: AuthTwinPort, targetPort: AuthTwinPort, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpHealthCheck("/realms/shareit/.well-known/openid-configuration");

var api = builder.AddDotnetProject("api", "backend/kroet.ShareIt.Api/kroet.ShareIt.Api.csproj")
    .WithHttpEndpoint(port: ApiPort, targetPort: ApiPort, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    // Match the API's "http" launch profile: local AuthTwin instead of the real Keycloak.
    .WithEnvironment("Auth__Authority", "http://localhost:" + AuthTwinPort + "/realms/shareit")
    .WithEnvironment("Auth__Audience", "shareit-api-dev")
    .WithEnvironment("Auth__RequireHttpsMetadata", "false")
    // Override the in-memory development stores with the containers above.
    .WithEnvironment("ContentStorage__PostgreSqlDb__ConnectionString", database.Resource.ConnectionStringExpression)
    .WithEnvironment("BlobStorage__S3__AccessKeyId", RustFsAccessKey)
    .WithEnvironment("BlobStorage__S3__SecretAccessKey", RustFsSecretKey)
    .WithEnvironment("BlobStorage__S3__ServiceUrl", rustFs.GetEndpoint("http"))
    .WithEnvironment("BlobStorage__S3__BucketName", "shareit-files")
    .WithHttpHealthCheck("/openapi/v1.json")
    .WaitFor(authTwin)
    .WaitFor(database)
    .WaitFor(rustFs);

builder.AddViteApp("frontend", "frontend")
    .WithPnpm()
    .WithHttpEndpoint(port: FrontendPort, targetPort: FrontendPort, name: "http", isProxied: false)
    .WaitFor(api);

builder.Build().Run();
