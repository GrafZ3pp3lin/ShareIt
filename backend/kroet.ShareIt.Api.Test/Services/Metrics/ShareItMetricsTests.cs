using System.Diagnostics.Metrics;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Services.Metrics;
using kroet.ShareIt.Api.Test.Infrastructure;

namespace kroet.ShareIt.Api.Test.Services.Metrics;

[TestClass]
public sealed class ShareItMetricsTests
{
    [TestMethod]
    public void RecordsCountersAndSnapshotGauges()
    {
        var captured = new List<MeasurementRecord>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ShareItMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            captured.Add(new MeasurementRecord(instrument.Name, value, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            captured.Add(new MeasurementRecord(instrument.Name, value, ToDictionary(tags))));
        listener.Start();

        using var meterFactory = new TestMeterFactory();
        var metrics = new ShareItMetrics(meterFactory);

        metrics.PackageCreated();
        metrics.PackageDeleted("owner");
        metrics.AccessClaimed();
        metrics.AccessSessionReused();
        metrics.ItemUploaded(ContentType.File, 11);
        metrics.DownloadAuthorized(11);
        metrics.UnlockFailed();
        metrics.RateLimited();

        metrics.SetSnapshot(new ApplicationMetricsSnapshot
        {
            TotalPackages = 5,
            ActivePackages = 3,
            ExpiredPackages = 1,
            ExhaustedPackages = 1,
            ProtectedPackages = 2,
            EmptyPackages = 4,
            TotalItems = 2,
            TotalBytes = 10,
            PendingBlobDeletions = 3,
            CleanupPending = 2
        });
        metrics.SetDependencyHealth(postgresUp: true, s3Up: false);
        metrics.SetCleanupLastSuccess(1_234);
        metrics.SetBuildInfo("1.0.0", "Testing");

        listener.RecordObservableInstruments();

        AssertMeasurement(captured, "shareit.packages.created", 1);
        AssertMeasurement(captured, "shareit.packages.deleted", 1, "reason", "owner");
        AssertMeasurement(captured, "shareit.package.accesses", 1);
        AssertMeasurement(captured, "shareit.items.uploaded", 1, "kind", "file");
        AssertMeasurement(captured, "shareit.bytes.uploaded", 11);
        AssertMeasurement(captured, "shareit.access.sessions_reused", 1);
        AssertMeasurement(captured, "shareit.bytes.authorized", 11);
        AssertMeasurement(captured, "shareit.unlock.failures", 1);
        AssertMeasurement(captured, "shareit.rate.limited", 1);

        AssertMeasurement(captured, "shareit.packages", 5, "state", "total");
        AssertMeasurement(captured, "shareit.packages", 3, "state", "active");
        AssertMeasurement(captured, "shareit.packages", 1, "state", "expired");
        AssertMeasurement(captured, "shareit.packages", 1, "state", "exhausted");
        AssertMeasurement(captured, "shareit.packages", 2, "state", "protected");
        AssertMeasurement(captured, "shareit.packages", 4, "state", "empty");
        AssertMeasurement(captured, "shareit.items", 2);
        AssertMeasurement(captured, "shareit.storage.bytes", 10);
        AssertMeasurement(captured, "shareit.blob_deletion.queue_depth", 3);
        AssertMeasurement(captured, "shareit.cleanup.pending", 2);
        AssertMeasurement(captured, "shareit.cleanup.last_success_timestamp_seconds", 1_234);
        AssertMeasurement(captured, "shareit.dependency.up", 1, "component", "postgres");
        AssertMeasurement(captured, "shareit.dependency.up", 0, "component", "s3");
        AssertMeasurement(captured, "shareit.build.info", 1, "environment", "Testing");
    }

    private static void AssertMeasurement(
        IReadOnlyCollection<MeasurementRecord> measurements,
        string name,
        double value,
        string? tagKey = null,
        object? tagValue = null)
    {
        var found = measurements.Any(measurement =>
            measurement.Name == name
            && Math.Abs(measurement.Value - value) < 0.001
            && (tagKey is null
                || (measurement.Tags.TryGetValue(tagKey, out var actual) && Equals(actual, tagValue))));

        Assert.IsTrue(
            found,
            $"Expected measurement '{name}' with value {value}" +
            (tagKey is null ? string.Empty : $" and tag {tagKey}={tagValue}") +
            $", but got: {string.Join(", ", measurements.Select(m => $"{m.Name}={m.Value}"))}");
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            result[tag.Key] = tag.Value;
        }

        return result;
    }

    private sealed record MeasurementRecord(
        string Name,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}
