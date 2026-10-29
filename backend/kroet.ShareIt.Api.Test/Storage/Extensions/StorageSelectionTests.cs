using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.Extensions;
using kroet.ShareIt.Api.Storage.InMemory;
using kroet.ShareIt.Api.Storage.Postgres;
using kroet.ShareIt.Api.Storage.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.Test.Storage.Extensions;

[TestClass]
public class StorageSelectionTests
{
    private static readonly Dictionary<string, string?> PostgresSettings = new()
    {
        ["ContentStorage:PostgreSqlDb:ConnectionString"] = "Host=localhost;Database=test"
    };

    private static readonly Dictionary<string, string?> InMemorySettings = new()
    {
        ["ContentStorage:InMemory:Enabled"] = "true"
    };

    private static readonly Dictionary<string, string?> S3Settings = new()
    {
        ["BlobStorage:S3:AccessKeyId"] = "access-key",
        ["BlobStorage:S3:SecretAccessKey"] = "secret-key",
        ["BlobStorage:S3:ServiceUrl"] = "http://localhost:9000",
        ["BlobStorage:S3:BucketName"] = "content"
    };

    private static readonly Dictionary<string, string?> InMemoryBlobSettings = new()
    {
        ["BlobStorage:InMemory:Enabled"] = "true"
    };

    [TestMethod]
    public void AddContentStorage_WithPostgresConfigured_RegistersSqlHandler()
    {
        var descriptor = GetSingleDescriptor(
            services => services.AddContentStorage(CreateConfiguration(PostgresSettings)),
            typeof(IContentStorageHandler));

        Assert.AreEqual(typeof(SqlContentStorageHandler), descriptor.ImplementationType);
    }

    [TestMethod]
    public void AddContentStorage_WithInMemoryConfigured_RegistersInMemoryHandler()
    {
        var descriptor = GetSingleDescriptor(
            services => services.AddContentStorage(CreateConfiguration(InMemorySettings)),
            typeof(IContentStorageHandler));

        Assert.AreEqual(typeof(InMemoryContentStorageHandler), descriptor.ImplementationType);
    }

    [TestMethod]
    public void AddContentStorage_WithNoStorageConfigured_Throws()
    {
        var services = new ServiceCollection();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => services.AddContentStorage(CreateConfiguration(new Dictionary<string, string?>())));
    }

    [TestMethod]
    public void AddBlobStorage_WithS3Configured_RegistersS3Handler()
    {
        var descriptor = GetSingleDescriptor(
            services => services.AddBlobStorage(CreateConfiguration(S3Settings)),
            typeof(IBlobStorageHandler));

        Assert.AreEqual(typeof(S3BlobStorageHandler), descriptor.ImplementationType);
    }

    [TestMethod]
    public void AddBlobStorage_WithInMemoryConfigured_RegistersInMemoryHandler()
    {
        var descriptor = GetSingleDescriptor(
            services => services.AddBlobStorage(CreateConfiguration(InMemoryBlobSettings)),
            typeof(IBlobStorageHandler));

        Assert.AreEqual(typeof(InMemoryBlobStorageHandler), descriptor.ImplementationType);
    }

    [TestMethod]
    public void AddBlobStorage_WithNoStorageConfigured_Throws()
    {
        var services = new ServiceCollection();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => services.AddBlobStorage(CreateConfiguration(new Dictionary<string, string?>())));
    }

    [TestMethod]
    public void AddDownloadTokenStorage_WithPostgresConfigured_RegistersSqlService()
    {
        var descriptor = GetSingleDescriptor(
            services => services.AddDownloadTokenStorage(CreateConfiguration(PostgresSettings)),
            typeof(IDownloadTokenService));

        Assert.AreEqual(typeof(SqlDownloadTokenService), descriptor.ImplementationType);
    }

    [TestMethod]
    public void AddDownloadTokenStorage_WithInMemoryConfigured_RegistersInMemoryService()
    {
        var descriptor = GetSingleDescriptor(
            services => services.AddDownloadTokenStorage(CreateConfiguration(InMemorySettings)),
            typeof(IDownloadTokenService));

        Assert.AreEqual(typeof(InMemoryDownloadTokenService), descriptor.ImplementationType);
    }

    [TestMethod]
    public void AddDownloadTokenStorage_WithNoStorageConfigured_Throws()
    {
        var services = new ServiceCollection();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => services.AddDownloadTokenStorage(CreateConfiguration(new Dictionary<string, string?>())));
    }

    private static ServiceDescriptor GetSingleDescriptor(
        Action<IServiceCollection> register,
        Type serviceType)
    {
        var services = new ServiceCollection();
        register(services);

        return services.Single(descriptor => descriptor.ServiceType == serviceType);
    }

    private static IConfiguration CreateConfiguration(IReadOnlyDictionary<string, string?> settings)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }
}
