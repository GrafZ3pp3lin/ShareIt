using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

public sealed class PostgresIntegrationDatabase : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;

    private PostgresIntegrationDatabase(PostgreSqlContainer container)
    {
        _container = container;
    }

    public string ConnectionString => _container.GetConnectionString();

    public static async Task<PostgresIntegrationDatabase> StartAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        var container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("shareit_integration")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await container.StartAsync();

        var database = new PostgresIntegrationDatabase(container);
        await database.InitializeSchemaAsync();

        return database;
    }

    public async Task ResetAsync()
    {
        const string truncateSql = """
                                   TRUNCATE TABLE download_tokens, contents, content_packages
                                   RESTART IDENTITY CASCADE;
                                   """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(truncateSql);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task InitializeSchemaAsync()
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Resources", "SqlSchema.sql");
        var schemaSql = await File.ReadAllTextAsync(schemaPath);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(schemaSql);
    }
}
