using System.Diagnostics;

namespace kroet.ShareIt.Api.Services.Observability;

/// <summary>
/// Activity source for application-level spans. Span names are deliberately
/// low-cardinality operation names; package and content identifiers belong in
/// the request URL and logs, not in the span name.
/// </summary>
public static class ShareItActivitySource
{
    /// <summary>
    /// Activity source name registered with OpenTelemetry.
    /// </summary>
    public const string Name = "ShareIt";

    public static readonly ActivitySource Instance = new(Name);
}
