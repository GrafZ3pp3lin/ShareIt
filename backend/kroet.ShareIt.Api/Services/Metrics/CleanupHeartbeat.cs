namespace kroet.ShareIt.Api.Services.Metrics;

/// <summary>
/// Records the last successful cleanup run so the metrics snapshot service can
/// expose its age as a gauge. The cleanup service runs in a background host,
/// so this is intentionally a small, thread-safe singleton.
/// </summary>
public sealed class CleanupHeartbeat
{
    private long _lastSuccessUnixSeconds;

    /// <summary>
    /// Unix timestamp (UTC seconds) of the last successful cleanup, or 0 when
    /// cleanup has not completed since this process started.
    /// </summary>
    public long LastSuccessUnixSeconds => Interlocked.Read(ref _lastSuccessUnixSeconds);

    /// <summary>
    /// Marks a cleanup run as successful.
    /// </summary>
    public void RecordSuccess(DateTimeOffset completedAt)
    {
        Interlocked.Exchange(ref _lastSuccessUnixSeconds, completedAt.ToUnixTimeSeconds());
    }
}
