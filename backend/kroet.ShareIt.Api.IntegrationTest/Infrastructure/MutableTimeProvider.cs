namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// A controllable <see cref="TimeProvider"/> for expiry tests.
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
