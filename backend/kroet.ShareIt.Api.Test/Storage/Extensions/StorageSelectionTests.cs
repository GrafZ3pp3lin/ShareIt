using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.Extensions;
using kroet.ShareIt.Api.Storage.InMemory;
using kroet.ShareIt.Api.Storage.Postgres;
using kroet.ShareIt.Api.Storage.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable SYSLIB0066 // IStartupValidator is what ValidateOnStart registers on this target.

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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(CreateConfiguration(PostgresSettings));
        services.AddContentStorage(CreateConfiguration(PostgresSettings));
        using var provider = services.BuildServiceProvider();

        Assert.IsInstanceOfType<SqlContentStorageHandler>(
            provider.GetRequiredService<IContentStorageHandler>());
    }

    [TestMethod]
    public void AddContentStorage_WithPostgresConfigured_RegistersMetricsSnapshotProviderUsingSameHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(CreateConfiguration(PostgresSettings));
        services.AddContentStorage(CreateConfiguration(PostgresSettings));
        using var provider = services.BuildServiceProvider();

        var contentStorage = provider.GetRequiredService<IContentStorageHandler>();
        var snapshotProvider = provider.GetRequiredService<IMetricsSnapshotProvider>();

        Assert.IsInstanceOfType<SqlContentStorageHandler>(snapshotProvider);
        Assert.IsTrue(ReferenceEquals(contentStorage, snapshotProvider));
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
    public void AddContentStorage_WithInMemoryConfigured_DoesNotRegisterMetricsSnapshotProvider()
    {
        var services = new ServiceCollection();
        services.AddContentStorage(CreateConfiguration(InMemorySettings));

        Assert.IsFalse(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IMetricsSnapshotProvider)));
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
    public void AddAccessSessionStorage_RegistersAccessSessionService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(CreateConfiguration(InMemorySettings));
        services.AddSingleton(TimeProvider.System);
        services.AddContentStorage(CreateConfiguration(InMemorySettings));
        services.AddAccessSessionStorage();
        using var provider = services.BuildServiceProvider();

        Assert.IsInstanceOfType<AccessSessionService>(
            provider.GetRequiredService<IAccessSessionService>());
    }

    [TestMethod]
    public void AddAccessSessionStorage_BindsAndValidatesDownloadSettings()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(CreateConfiguration(new Dictionary<string, string?>
        {
            ["DownloadSettings:AccessSessionLifetimeMinutes"] = "15",
            ["DownloadSettings:ExhaustedPackageRetentionMinutes"] = "30"
        }));
        services.AddAccessSessionStorage();
        using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<kroet.ShareIt.Api.Configuration.DownloadSettings>>();
        Assert.AreEqual(15, settings.Value.AccessSessionLifetimeMinutes);
        Assert.AreEqual(30, settings.Value.ExhaustedPackageRetentionMinutes);
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [TestMethod]
    public void AddAccessSessionStorage_ValidateOnStartRejectsRetentionShorterThanSessionLifetime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(CreateConfiguration(new Dictionary<string, string?>
        {
            ["DownloadSettings:AccessSessionLifetimeMinutes"] = "30",
            ["DownloadSettings:ExhaustedPackageRetentionMinutes"] = "15"
        }));
        services.AddAccessSessionStorage();
        using var provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
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
