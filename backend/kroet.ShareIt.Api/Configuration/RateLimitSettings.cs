using FluentValidation;

namespace kroet.ShareIt.Api.Configuration;

/// <summary>
/// Rate limits for reading a package link. The limiter is partitioned on
/// (package id, remote IP), so unlocking a link counts towards the same budget
/// as failed attempts and one NAT cannot lock everyone out.
/// </summary>
public sealed class RateLimitSettings : ISettings
{
    /// <summary>
    /// Requests allowed per (package id, remote IP) window.
    /// Default: 20.
    /// </summary>
    public int PackageReadPermitLimit { get; set; } = 20;

    /// <summary>
    /// Length of the sliding window in minutes.
    /// Default: 15.
    /// </summary>
    public int PackageReadWindowMinutes { get; set; } = 15;

    public static string SectionName => "RateLimitSettings";
}

/// <summary>
/// Names of the rate-limit policies attached to endpoints.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Limits reads of a package link, partitioned on (package id, remote IP).
    /// </summary>
    public const string PackageRead = "package-read";
}

public sealed class RateLimitSettingsValidator : AbstractValidator<RateLimitSettings>
{
    public RateLimitSettingsValidator()
    {
        RuleFor(x => x.PackageReadPermitLimit).GreaterThan(0);

        RuleFor(x => x.PackageReadWindowMinutes).GreaterThan(0);
    }
}
