using System.Diagnostics.Metrics;
using kroet.ShareIt.Api.Models;

namespace kroet.ShareIt.Api.Services.Metrics;

/// <summary>
/// Application metrics exported through OpenTelemetry to the OTLP collector.
/// Labels are deliberately bounded to a small set of values; package, user and
/// content identifiers must never be added here.
/// </summary>
public sealed class ShareItMetrics : IDisposable
{
    /// <summary>
    /// Meter name registered with OpenTelemetry.
    /// </summary>
    public const string MeterName = "ShareIt";

    private static readonly ApplicationMetricsSnapshot EmptySnapshot = new()
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
    };

    private static readonly BuildInfoState UnknownBuildInfo = new("unknown", "unknown");

    private readonly Meter _meter;
    private readonly Counter<long> _packagesCreated;
    private readonly Counter<long> _packagesDeleted;
    private readonly Counter<long> _packageAccesses;
    private readonly Counter<long> _accessSessionsReused;
    private readonly Counter<long> _itemsUploaded;
    private readonly Counter<long> _bytesUploaded;
    private readonly Counter<long> _authorizedBytes;
    private readonly Counter<long> _unlockFailures;
    private readonly Counter<long> _rateLimited;
    private readonly Counter<long> _shortCodeLookups;
    private readonly Counter<long> _shortCodeAllocations;
    private readonly Counter<long> _shortCodeBurns;

    private readonly ObservableGauge<int> _packages;
    private readonly ObservableGauge<int> _items;
    private readonly ObservableGauge<long> _storageBytes;
    private readonly ObservableGauge<int> _pendingBlobDeletions;
    private readonly ObservableGauge<int> _cleanupPending;
    private readonly ObservableGauge<long> _cleanupLastSuccess;
    private readonly ObservableGauge<int> _dependencyUp;
    private readonly ObservableGauge<int> _buildInfo;

    private ApplicationMetricsSnapshot _snapshot = EmptySnapshot;
    private long _cleanupLastSuccessUnixSeconds;
    private int _postgresUp;
    private int _s3Up;
    private BuildInfoState _buildInfoState = UnknownBuildInfo;

    public ShareItMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _packagesCreated = _meter.CreateCounter<long>(
            "shareit.packages.created",
            unit: "{package}",
            description: "Number of content packages created.");

        _packagesDeleted = _meter.CreateCounter<long>(
            "shareit.packages.deleted",
            unit: "{package}",
            description: "Number of content packages deleted, by reason.");

        _packageAccesses = _meter.CreateCounter<long>(
            "shareit.package.accesses",
            unit: "{access}",
            description: "Number of package access claims.");

        _accessSessionsReused = _meter.CreateCounter<long>(
            "shareit.access.sessions_reused",
            unit: "{request}",
            description: "Number of package reads that reused an existing access session instead of claiming another access.");

        _itemsUploaded = _meter.CreateCounter<long>(
            "shareit.items.uploaded",
            unit: "{item}",
            description: "Number of content items uploaded, by kind.");

        _bytesUploaded = _meter.CreateCounter<long>(
            "shareit.bytes.uploaded",
            unit: "By",
            description: "Number of content bytes uploaded.");

        _authorizedBytes = _meter.CreateCounter<long>(
            "shareit.bytes.authorized",
            unit: "By",
            description: "Logical content size whose download was authorized by an access session. The full content size is recorded even if the response is interrupted.");

        _unlockFailures = _meter.CreateCounter<long>(
            "shareit.unlock.failures",
            unit: "{attempt}",
            description: "Number of failed package unlock attempts.");

        _rateLimited = _meter.CreateCounter<long>(
            "shareit.rate.limited",
            unit: "{request}",
            description: "Number of requests rejected by the package-read, short-code, download or upload rate limiter.");

        _shortCodeLookups = _meter.CreateCounter<long>(
            "shareit.short_code.lookups",
            unit: "{request}",
            description: "Short-code lookup results, by bounded result value.");

        _shortCodeAllocations = _meter.CreateCounter<long>(
            "shareit.short_code.allocations",
            unit: "{request}",
            description: "Short-code allocation results, by bounded result value.");

        _shortCodeBurns = _meter.CreateCounter<long>(
            "shareit.short_code.burns",
            unit: "{package}",
            description: "Short-code packages burned after five failed unlock proofs.");

        _packages = _meter.CreateObservableGauge(
            "shareit.packages",
            ObservePackages,
            unit: "{package}",
            description: "Current number of packages, by state.");

        _items = _meter.CreateObservableGauge(
            "shareit.items",
            () => Volatile.Read(ref _snapshot).TotalItems,
            unit: "{item}",
            description: "Current number of stored content items.");

        _storageBytes = _meter.CreateObservableGauge(
            "shareit.storage.bytes",
            () => Volatile.Read(ref _snapshot).TotalBytes,
            unit: "By",
            description: "Current logical size of all stored content, in bytes.");

        _pendingBlobDeletions = _meter.CreateObservableGauge(
            "shareit.blob_deletion.queue_depth",
            () => Volatile.Read(ref _snapshot).PendingBlobDeletions,
            unit: "{job}",
            description: "Number of queued blob deletions waiting for the cleanup service.");

        _cleanupPending = _meter.CreateObservableGauge(
            "shareit.cleanup.pending",
            () => Volatile.Read(ref _snapshot).CleanupPending,
            unit: "{package}",
            description: "Number of packages currently eligible for cleanup.");

        _cleanupLastSuccess = _meter.CreateObservableGauge(
            "shareit.cleanup.last_success_timestamp_seconds",
            () => Interlocked.Read(ref _cleanupLastSuccessUnixSeconds),
            unit: "s",
            description: "Unix timestamp of the last successful cleanup run, or 0 if none has completed.");

        _dependencyUp = _meter.CreateObservableGauge(
            "shareit.dependency.up",
            ObserveDependencyHealth,
            unit: "1",
            description: "Whether an application dependency is reachable: 1 for up, 0 for down.");

        _buildInfo = _meter.CreateObservableGauge(
            "shareit.build.info",
            ObserveBuildInfo,
            unit: "1",
            description: "Build information for the running API instance.");
    }

    public void PackageCreated(int count = 1)
    {
        if (count > 0)
        {
            _packagesCreated.Add(count);
        }
    }

    public void PackageDeleted(string reason, int count = 1)
    {
        if (count > 0)
        {
            _packagesDeleted.Add(count, new KeyValuePair<string, object?>("reason", reason));
        }
    }

    public void AccessClaimed()
    {
        _packageAccesses.Add(1);
    }

    public void AccessSessionReused()
    {
        _accessSessionsReused.Add(1);
    }

    public void ItemUploaded(ContentType contentType, long size)
    {
        var kind = contentType == ContentType.Text ? "text" : "file";
        _itemsUploaded.Add(1, new KeyValuePair<string, object?>("kind", kind));
        _bytesUploaded.Add(size);
    }

    public void DownloadAuthorized(long authorizedBytes)
    {
        _authorizedBytes.Add(authorizedBytes);
    }

    public void UnlockFailed()
    {
        _unlockFailures.Add(1);
    }

    public void RateLimited()
    {
        _rateLimited.Add(1);
    }

    public void ShortCodeLookup(string result)
    {
        _shortCodeLookups.Add(1, new KeyValuePair<string, object?>("result", result));
    }

    public void ShortCodeAllocation(string result)
    {
        _shortCodeAllocations.Add(1, new KeyValuePair<string, object?>("result", result));
    }

    public void ShortCodeBurned()
    {
        _shortCodeBurns.Add(1);
    }

    public void SetSnapshot(ApplicationMetricsSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
    }

    public void SetDependencyHealth(bool postgresUp, bool s3Up)
    {
        Volatile.Write(ref _postgresUp, postgresUp ? 1 : 0);
        Volatile.Write(ref _s3Up, s3Up ? 1 : 0);
    }

    public void SetCleanupLastSuccess(long unixSeconds)
    {
        Interlocked.Exchange(ref _cleanupLastSuccessUnixSeconds, unixSeconds);
    }

    public void SetBuildInfo(string version, string environment)
    {
        Volatile.Write(ref _buildInfoState, new BuildInfoState(version, environment));
    }

    private IEnumerable<Measurement<int>> ObservePackages()
    {
        var snapshot = Volatile.Read(ref _snapshot);

        yield return new Measurement<int>(
            snapshot.TotalPackages,
            new KeyValuePair<string, object?>("state", "total"));
        yield return new Measurement<int>(
            snapshot.ActivePackages,
            new KeyValuePair<string, object?>("state", "active"));
        yield return new Measurement<int>(
            snapshot.ExpiredPackages,
            new KeyValuePair<string, object?>("state", "expired"));
        yield return new Measurement<int>(
            snapshot.ExhaustedPackages,
            new KeyValuePair<string, object?>("state", "exhausted"));
        yield return new Measurement<int>(
            snapshot.ProtectedPackages,
            new KeyValuePair<string, object?>("state", "protected"));
        yield return new Measurement<int>(
            snapshot.EmptyPackages,
            new KeyValuePair<string, object?>("state", "empty"));
    }

    private IEnumerable<Measurement<int>> ObserveDependencyHealth()
    {
        yield return new Measurement<int>(
            Volatile.Read(ref _postgresUp),
            new KeyValuePair<string, object?>("component", "postgres"));
        yield return new Measurement<int>(
            Volatile.Read(ref _s3Up),
            new KeyValuePair<string, object?>("component", "s3"));
    }

    private Measurement<int> ObserveBuildInfo()
    {
        var buildInfo = Volatile.Read(ref _buildInfoState);

        return new Measurement<int>(
            1,
            new KeyValuePair<string, object?>("version", buildInfo.Version),
            new KeyValuePair<string, object?>("environment", buildInfo.Environment));
    }

    private sealed record BuildInfoState(string Version, string Environment);

    /// <summary>
    /// Stops recording the instruments. The meter factory normally disposes
    /// created meters with itself; keeping the reference here also prevents
    /// premature collection.
    /// </summary>
    public void Dispose()
    {
        _meter.Dispose();
        GC.SuppressFinalize(this);
    }
}
