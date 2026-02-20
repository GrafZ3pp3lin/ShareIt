using FluentValidation;

namespace kroet.ShareIt.Api.Content.Configuration;

public class CleanupSettings
{
    /// <summary>
    /// Interval in minutes for the background cleanup service.
    /// Default: 5 minutes.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 5;

    public static string SectionName => "CleanupSettings";
}

public class CleanupSettingsValidator : AbstractValidator<CleanupSettings>
{
    public CleanupSettingsValidator()
    {
        RuleFor(x => x.CleanupIntervalMinutes).GreaterThan(0);
    }
}