using System.Collections.Concurrent;
using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// The unlock proof is a bearer secret. Failures may be logged with the
/// package id, but never with the header value.
/// </summary>
[TestClass]
public sealed class ProofLogLeakTests : ShareItApiTestBase
{
    private readonly CapturingLoggerProvider _logs = new();

    protected override Action<IServiceCollection>? ConfigureTestServices =>
        services =>
        {
            services.RemoveAll<ILoggerFactory>();
            services.AddLogging(builder => builder.ClearProviders().AddProvider(_logs));
        };

    [TestMethod]
    public async Task FailedUnlock_DoesNotLogTheProof()
    {
        Services.GetRequiredService<ILogger<ProofLogLeakTests>>().LogWarning("capture probe");
        Assert.IsTrue(_logs.JoinedMessages.Contains("capture probe", StringComparison.Ordinal), _logs.JoinedMessages);

        var ownerId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            userId: ownerId, maxAccessCount: 1, timeToLiveHours: 1, unlockVerifier: Verifier());

        var proof = Verifier(7);
        var response = await GetPackageAsync(packageId, guestId: Guid.NewGuid(), unlockProof: proof);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        var logs = _logs.JoinedMessages;
        StringAssert.Contains(logs, "Failed unlock attempt", "The failed unlock should be logged.");
        Assert.IsFalse(
            logs.Contains(proof, StringComparison.Ordinal),
            "The unlock proof header value must never appear in logs.");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public string JoinedMessages => string.Join('\n', _messages);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages, string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue($"{categoryName}: {formatter(state, exception)}");
            }
        }
    }
}
