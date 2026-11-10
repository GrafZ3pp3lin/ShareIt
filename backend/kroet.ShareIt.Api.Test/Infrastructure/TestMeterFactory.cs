using System.Diagnostics.Metrics;

namespace kroet.ShareIt.Api.Test.Infrastructure;

/// <summary>
/// Minimal <see cref="IMeterFactory"/> for unit tests. It keeps the created
/// meters alive for the lifetime of the factory and does not require a host.
/// </summary>
public sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(string name)
        => Create(new MeterOptions(name));

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
        {
            meter.Dispose();
        }

        _meters.Clear();
    }
}
