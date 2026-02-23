using Dapper;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Storage.InMemory;
using kroet.ShareIt.Api.Storage.Postgres;
using Npgsql;

namespace kroet.ShareIt.Api.Storage.Extensions;

/// <summary>
/// Extension methods for configuring content package storage.
/// </summary>
public static class ContentStorageExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds SQL-based content package storage.
        /// </summary>
        public IServiceCollection AddContentStorage(IConfiguration configuration)
        {
            if (configuration.GetSection(SqlStorageSettings.SectionName).Exists())
            {
                services.AddSqlContentStorage();
            }
            if (configuration.GetSection(InMemoryContentStorageSettings.SectionName).Exists())
            {
                services.AddInMemoryContentStorage();
            }
            else
            {
                throw new InvalidOperationException("No valid content storage configuration found.");
            }

            return services;
        }

        private IServiceCollection AddSqlContentStorage()
        {
            services.AddOptions<SqlStorageSettings>()
                .BindConfiguration(SqlStorageSettings.SectionName)
                .ValidateWithFluentValidator<SqlStorageSettings, PostgreSqlDbSettingsValidator>()
                .ValidateOnStart();
        
            services.AddSingleton<IContentStorageHandler, SqlContentStorageHandler>();

            return services;
        }
        
        private IServiceCollection AddInMemoryContentStorage()
        {
            services.AddSingleton<IContentStorageHandler, InMemoryContentStorageHandler>();

            return services;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>
        /// Initializes the SQL package storage schema.
        /// Call this during application startup.
        /// </summary>
        public Task InitializePackageStorageAsync()
        {
            if (app.Configuration.GetSection(SqlStorageSettings.SectionName).Exists())
            {
                return app.InitializePostgresPackageStorageAsync();
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Initializes the SQL package storage schema.
        /// Call this during application startup.
        /// </summary>
        private async Task InitializePostgresPackageStorageAsync()
        {
            var settings = app.Services.GetRequiredService<SqlStorageSettings>();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            if (!settings.AutoInitializeSchema)
            {
                logger.LogDebug("Skipping SQL package storage schema initialization as per configuration");
                return;
            }
        
            try
            {
                logger.LogDebug("Initializing SQL package storage schema...");
                await InitializeDatabaseAsync(settings.ConnectionString, app.Lifetime.ApplicationStopping);
                logger.LogInformation("SQL package storage schema initialized successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize SQL package storage schema");
                throw;
            }
        }
    }

    /// <summary>
    /// Initializes the database schema. Should be called on application startup.
    /// </summary>
    private static async Task InitializeDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Read and execute schema from embedded resource or file
        var schemaScript = await File.ReadAllTextAsync("./Resources/SqlSchema.sql", cancellationToken);

        await connection.ExecuteAsync(schemaScript);
    }
}

