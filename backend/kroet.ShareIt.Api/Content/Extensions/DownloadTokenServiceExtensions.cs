using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content.Configuration;
using kroet.ShareIt.Api.Content.Services;

namespace kroet.ShareIt.Api.Content.Extensions;

public static class DownloadTokenServiceExtensions
{
    public static IServiceCollection AddDownloadTokenService(this IServiceCollection services)
    {
        services.AddOptions<DownloadSettings>()
            .BindConfiguration(DownloadSettings.SectionName)
            .ValidateWithFluentValidator<DownloadSettings, DownloadSettingsValidator>()
            .ValidateOnStart();
        
        services.AddSingleton<IDownloadTokenService, SqlDownloadTokenService>();

        return services;
    }
}