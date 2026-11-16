using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using kroet.ShareIt.Api.Services.Cleanup;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Services.Observability;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Test.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace kroet.ShareIt.Api.Test.Services.Cleanup;

[TestClass]
public class CleanupServiceTests
{
    private readonly IDownloadTokenService _tokenService = Substitute.For<IDownloadTokenService>();
    private readonly IContentStorageHandler _contentStorageHandler = Substitute.For<IContentStorageHandler>();
    private readonly IStorageHandler _storageHandler = Substitute.For<IStorageHandler>();
    private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceProvider _scopeProvider = Substitute.For<IServiceProvider>();
    private readonly CleanupHeartbeat _heartbeat = new();
    private readonly ShareItMetrics _metrics = new(new TestMeterFactory());
    private readonly CleanupService _subject;

    public CleanupServiceTests()
    {
        _scopeProvider.GetService(typeof(IDownloadTokenService)).Returns(_tokenService);
        _scopeProvider.GetService(typeof(IContentStorageHandler)).Returns(_contentStorageHandler);
        _scopeProvider.GetService(typeof(IStorageHandler)).Returns(_storageHandler);
        _scope.ServiceProvider.Returns(_scopeProvider);
        _scopeFactory.CreateScope().Returns(_scope);
        _storageHandler
            .ProcessPendingBlobDeletionsAsync(Arg.Any<CancellationToken>())
            .Returns(0);

        _subject = new CleanupService(
            _scopeFactory,
            Options.Create(new CleanupSettings { CleanupIntervalMinutes = 1 }),
            TimeProvider.System,
            _heartbeat,
            _metrics,
            NullLogger<CleanupService>.Instance);
    }

    [TestMethod]
    public async Task RunCleanupAsync_DeletesExpiredTokensAndPackages()
    {
        var firstPackageId = Guid.NewGuid();
        var secondPackageId = Guid.NewGuid();
        _tokenService.CleanupExpiredTokensAsync(Arg.Any<CancellationToken>()).Returns(2);
        _contentStorageHandler.GetPackagesToDeleteAsync(Arg.Any<CancellationToken>())
            .Returns([firstPackageId, secondPackageId]);
        _storageHandler.DeletePackagesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<CancellationToken>()).Returns(1);

        await InvokeRunCleanupAsync();

        await _tokenService.Received(1).CleanupExpiredTokensAsync(Arg.Any<CancellationToken>());
        await _storageHandler.Received(1).DeletePackagesAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { firstPackageId, secondPackageId })),
            Arg.Any<CancellationToken>());
        await _storageHandler.Received(1).ProcessPendingBlobDeletionsAsync(Arg.Any<CancellationToken>());
        Assert.AreNotEqual(0, _heartbeat.LastSuccessUnixSeconds);
    }

    [TestMethod]
    public async Task RunCleanupAsync_WhenPackageDeletionFails_DoesNotThrow()
    {
        _tokenService.CleanupExpiredTokensAsync(Arg.Any<CancellationToken>()).Returns(1);
        _contentStorageHandler.GetPackagesToDeleteAsync(Arg.Any<CancellationToken>())
            .Returns([Guid.NewGuid()]);
        _storageHandler.DeletePackagesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<CancellationToken>()).Returns(
                Task.FromException<int>(new InvalidOperationException("delete failed")));

        await InvokeRunCleanupAsync();

        await _tokenService.Received(1).CleanupExpiredTokensAsync(Arg.Any<CancellationToken>());
        await _storageHandler.Received(1).DeletePackagesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<CancellationToken>());
        await _storageHandler.Received(1).ProcessPendingBlobDeletionsAsync(Arg.Any<CancellationToken>());
        Assert.AreEqual(0, _heartbeat.LastSuccessUnixSeconds);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCancelled_StopsCleanly()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var executeMethod = typeof(CleanupService).GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)executeMethod.Invoke(_subject, [cancellationTokenSource.Token])!;

        await _tokenService.DidNotReceiveWithAnyArgs().CleanupExpiredTokensAsync(default);
    }

    [TestMethod]
    public async Task RunCleanupAsync_EmitsCleanupSpanWithCounts()
    {
        _tokenService.CleanupExpiredTokensAsync(Arg.Any<CancellationToken>()).Returns(42);
        _contentStorageHandler.GetPackagesToDeleteAsync(Arg.Any<CancellationToken>())
            .Returns([Guid.NewGuid(), Guid.NewGuid()]);
        _storageHandler.DeletePackagesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<CancellationToken>()).Returns(1);
        _storageHandler
            .ProcessPendingBlobDeletionsAsync(Arg.Any<CancellationToken>())
            .Returns(3);

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ShareItActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        await InvokeRunCleanupAsync();

        var span = activities.SingleOrDefault(activity =>
            activity.OperationName == "cleanup.run" &&
            activity.GetTagItem("shareit.cleanup.tokens_deleted") is 42L);
        Assert.IsNotNull(span);
        Assert.AreEqual(2, span.GetTagItem("shareit.cleanup.packages_pending"));
        Assert.AreEqual(1, span.GetTagItem("shareit.cleanup.packages_deleted"));
        Assert.AreEqual(3, span.GetTagItem("shareit.cleanup.blobs_deleted"));
        Assert.AreEqual(true, span.GetTagItem("shareit.cleanup.blob_drain_succeeded"));
        Assert.AreEqual(true, span.GetTagItem("shareit.cleanup.succeeded"));
    }

    [TestMethod]
    public async Task RunCleanupAsync_WhenTokenCleanupFails_EmitsErrorSpan()
    {
        _tokenService.CleanupExpiredTokensAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<long>(new InvalidOperationException("token cleanup failed")));

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ShareItActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        await InvokeRunCleanupAsync();

        var span = activities.SingleOrDefault(activity =>
            activity.OperationName == "cleanup.run" &&
            activity.StatusDescription == "cleanup failed");
        Assert.IsNotNull(span);
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual(false, span.GetTagItem("shareit.cleanup.succeeded"));
        Assert.AreEqual(0, _heartbeat.LastSuccessUnixSeconds);
    }

    [TestMethod]
    public async Task RunCleanupAsync_WhenBlobQueueDrainFails_StillEmitsDeletedMetricAndHeartbeat()
    {
        _tokenService.CleanupExpiredTokensAsync(Arg.Any<CancellationToken>()).Returns(0);
        _contentStorageHandler.GetPackagesToDeleteAsync(Arg.Any<CancellationToken>())
            .Returns([Guid.NewGuid(), Guid.NewGuid()]);
        _storageHandler.DeletePackagesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<CancellationToken>()).Returns(7);
        _storageHandler
            .ProcessPendingBlobDeletionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("queue read failed")));

        var measurements = new ConcurrentBag<(string Name, long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ShareItMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                tagMap[tag.Key] = tag.Value;
            }

            measurements.Add((instrument.Name, value, tagMap));
        });
        listener.Start();

        await InvokeRunCleanupAsync();

        var deleted = measurements.SingleOrDefault(measurement =>
            measurement.Name == "shareit.packages.deleted" &&
            measurement.Value == 7 &&
            Equals(measurement.Tags.GetValueOrDefault("reason"), "cleanup"));
        Assert.IsNotNull(deleted.Name);
        Assert.AreNotEqual(
            0,
            _heartbeat.LastSuccessUnixSeconds,
            "Queue draining is independent of the package metadata deletion that already committed.");
    }

    private async Task InvokeRunCleanupAsync()
    {
        var cleanupMethod = typeof(CleanupService).GetMethod(
            "RunCleanupAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)cleanupMethod.Invoke(_subject, [CancellationToken.None])!;
    }
}
