namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

[TestClass]
public static class IntegrationTestAssemblyCleanup
{
    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        await EndpointTestStorage.DisposeAsync();
    }
}
