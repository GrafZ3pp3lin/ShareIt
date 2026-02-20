using FluentValidation;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content.Configuration;
using kroet.ShareIt.Api.Content.Services;

namespace kroet.ShareIt.Api.Content.Extensions;

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