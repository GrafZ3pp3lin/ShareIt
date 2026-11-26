using FluentValidation;
using kroet.ShareIt.Api.Configuration;

namespace kroet.ShareIt.Api.Services.Cleanup;

public static class CleanupServiceExtensions
{
    public static IServiceCollection AddCleanupService(this IServiceCollection services)
    {
        services.AddScoped<IValidator<CleanupSettings>, CleanupSettingsValidator>();

        services.AddOptions<CleanupSettings>()
            .BindConfiguration(CleanupSettings.SectionName)
            .ValidateWithFluentValidator<CleanupSettings, CleanupSettingsValidator>()
            .ValidateOnStart();

        services.AddHostedService<CleanupService>();

        return services;
    }
}
