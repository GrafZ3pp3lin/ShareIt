using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Starts a RustFS container for S3-compatible blob storage integration tests.
/// </summary>
internal sealed class RustFsContainer : IAsyncDisposable
{
    private const int S3Port = 9000;
    private const string AccessKey = "shareit-integration-access-key";
    private const string SecretKey = "shareit-integration-secret-key";

    private readonly IContainer _container;

    private RustFsContainer(IContainer container)
    {
        _container = container;
    }

    public string AccessKeyId => AccessKey;

    public string SecretAccessKey => SecretKey;

    public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(S3Port)}";

    public static async Task<RustFsContainer> StartAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        var container = new ContainerBuilder("rustfs/rustfs:1.0.4")
            .WithEnvironment("RUSTFS_ACCESS_KEY", AccessKey)
            .WithEnvironment("RUSTFS_SECRET_KEY", SecretKey)
            .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "false")
            .WithEnvironment("RUSTFS_CHECK_UPDATE", "false")
            .WithEnvironment("RUSTFS_OBS_LOGGER_LEVEL", "error")
            .WithPortBinding(S3Port, true)
            .WithCommand("/data")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request
                    .ForPort(S3Port)
                    .ForPath("/health")))
            .Build();

        await container.StartAsync();

        return new RustFsContainer(container);
    }

    /// <summary>
    /// Creates an S3 client configured like the handler under test, for inspecting stored objects.
    /// </summary>
    public AmazonS3Client CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = ServiceUrl,
            ForcePathStyle = true,
        };

        return new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), config);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
