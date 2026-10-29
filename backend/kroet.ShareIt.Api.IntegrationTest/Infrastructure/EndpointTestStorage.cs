namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Lazily starts the real Postgres and RustFS containers shared by all
/// endpoint/TestServer tests. Starting lazily keeps filtered runs that do not
/// touch endpoints from pulling containers.
/// </summary>
internal static class EndpointTestStorage
{
    private static readonly SemaphoreSlim StartLock = new(1, 1);

    private static PostgresIntegrationDatabase? _database;
    private static RustFsContainer? _rustFs;

    public static PostgresIntegrationDatabase Database =>
        _database ?? throw new InvalidOperationException("Endpoint test storage has not been started.");

    public static RustFsContainer RustFs =>
        _rustFs ?? throw new InvalidOperationException("Endpoint test storage has not been started.");

    public static async Task EnsureStartedAsync()
    {
        if (_database is not null && _rustFs is not null)
        {
            return;
        }

        await StartLock.WaitAsync();
        try
        {
            _database ??= await PostgresIntegrationDatabase.StartAsync();
            _rustFs ??= await RustFsContainer.StartAsync();
        }
        finally
        {
            StartLock.Release();
        }
    }

    public static Task ResetDatabaseAsync() => Database.ResetAsync();

    public static async Task DisposeAsync()
    {
        if (_rustFs is not null)
        {
            await _rustFs.DisposeAsync();
            _rustFs = null;
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
            _database = null;
        }
    }
}
