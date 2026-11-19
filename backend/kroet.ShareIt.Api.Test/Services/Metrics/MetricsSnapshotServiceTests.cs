using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Test.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace kroet.ShareIt.Api.Test.Services.Metrics;

[TestClass]
public sealed class MetricsSnapshotServiceTests
{
    private readonly IMetricsSnapshotProvider _snapshotProvider = Substitute.For<IMetricsSnapshotProvider>();
    private readonly IBlobStorageHandler _blobStorageHandler = Substitute.For<IBlobStorageHandler>();
    private readonly CleanupHeartbeat _heartbeat = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly ConcurrentBag<MeasurementRecord> _measurements = [];
    private readonly MeterListener _listener = new();
    private readonly ShareItMetrics _metrics;
    private readonly MetricsSnapshotService _subject;

    public MetricsSnapshotServiceTests()
    {
        _metrics = new ShareItMetrics(_meterFactory);
        _subject = new MetricsSnapshotService(
            _snapshotProvider,
            _blobStorageHandler,
            _heartbeat,
            _metrics,
            TimeProvider.System,
            NullLogger<MetricsSnapshotService>.Instance);

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ShareItMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            AddMeasurement(instrument.Name, value, tags));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            AddMeasurement(instrument.Name, value, tags));
        _listener.Start();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _listener.Dispose();
        _meterFactory.Dispose();
    }

    [TestMethod]
    public async Task RefreshAsync_UpdatesSnapshotDependencyAndHeartbeatGauges()
    {
        _snapshotProvider
            .GetMetricsSnapshotAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ApplicationMetricsSnapshot
            {
                TotalPackages = 5,
                ActivePackages = 3,
                ExpiredPackages = 1,
                ExhaustedPackages = 1,
                ProtectedPackages = 2,
                EmptyPackages = 4,
                TotalItems = 7,
                TotalBytes = 1234,
                PendingBlobDeletions = 5,
                CleanupPending = 2
            });
        _blobStorageHandler.CheckHealthAsync(Arg.Any<CancellationToken>()).Returns(true);
        _heartbeat.RecordSuccess(DateTimeOffset.FromUnixTimeSeconds(1_234));

        await InvokeRefreshAsync();

        _listener.RecordObservableInstruments();

        AssertMeasurement("shareit.packages", 5, "state", "total");
        AssertMeasurement("shareit.items", 7);
        AssertMeasurement("shareit.storage.bytes", 1234);
        AssertMeasurement("shareit.blob_deletion.queue_depth", 5);
        AssertMeasurement("shareit.cleanup.pending", 2);
        AssertMeasurement("shareit.cleanup.last_success_timestamp_seconds", 1_234);
        AssertMeasurement("shareit.dependency.up", 1, "component", "postgres");
        AssertMeasurement("shareit.dependency.up", 1, "component", "s3");
    }

    [TestMethod]
    public async Task RefreshAsync_WhenSnapshotFails_MarksPostgresDownAndS3Up()
    {
        _snapshotProvider
            .GetMetricsSnapshotAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ApplicationMetricsSnapshot>(
                new InvalidOperationException("database unavailable")));
        _blobStorageHandler.CheckHealthAsync(Arg.Any<CancellationToken>()).Returns(true);

        await InvokeRefreshAsync();

        _listener.RecordObservableInstruments();

        AssertMeasurement("shareit.dependency.up", 0, "component", "postgres");
        AssertMeasurement("shareit.dependency.up", 1, "component", "s3");
    }

    [TestMethod]
    public async Task RefreshAsync_WhenBlobHealthThrows_MarksS3Down()
    {
        _snapshotProvider
            .GetMetricsSnapshotAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ApplicationMetricsSnapshot
            {
                TotalPackages = 0,
                ActivePackages = 0,
                ExpiredPackages = 0,
                ExhaustedPackages = 0,
                ProtectedPackages = 0,
                EmptyPackages = 0,
                TotalItems = 0,
                TotalBytes = 0,
                PendingBlobDeletions = 0,
                CleanupPending = 0
            });
        _blobStorageHandler.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("s3 unavailable")));

        await InvokeRefreshAsync();

        _listener.RecordObservableInstruments();

        AssertMeasurement("shareit.dependency.up", 1, "component", "postgres");
        AssertMeasurement("shareit.dependency.up", 0, "component", "s3");
    }

    private Task InvokeRefreshAsync()
    {
        var refreshMethod = typeof(MetricsSnapshotService).GetMethod(
            "RefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (Task)refreshMethod.Invoke(_subject, [CancellationToken.None])!;
    }

    private void AddMeasurement(
        string name,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var tagMap = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            tagMap[tag.Key] = tag.Value;
        }

        _measurements.Add(new MeasurementRecord(name, value, tagMap));
    }

    private void AssertMeasurement(
        string name,
        double value,
        string? tagKey = null,
        object? tagValue = null)
    {
        var found = _measurements.Any(measurement =>
            measurement.Name == name
            && Math.Abs(measurement.Value - value) < 0.001
            && (tagKey is null
                || (measurement.Tags.TryGetValue(tagKey, out var actual) && Equals(actual, tagValue))));

        Assert.IsTrue(
            found,
            $"Expected measurement '{name}' with value {value}" +
            (tagKey is null ? string.Empty : $" and tag {tagKey}={tagValue}") +
            $", but got: {string.Join(", ", _measurements.Select(m => $"{m.Name}={m.Value}"))}");
    }

    private sealed record MeasurementRecord(
        string Name,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}
