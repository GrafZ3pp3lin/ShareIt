namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

[TestClass]
public sealed class IntegrationTestAssemblyCleanup
{
    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        await EndpointTestStorage.DisposeAsync();
    }
}
