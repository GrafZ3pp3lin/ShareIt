using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Storage.InMemory;
using kroet.ShareIt.Api.Storage.Postgres;

namespace kroet.ShareIt.Api.Storage.Extensions;

public static class DownloadTokenStorageExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDownloadTokenStorage(IConfiguration configuration)
        {
            services.AddOptions<DownloadSettings>()
                .BindConfiguration(DownloadSettings.SectionName)
                .ValidateWithFluentValidator<DownloadSettings, DownloadSettingsValidator>()
                .ValidateOnStart();
            
            if (configuration.GetSection(SqlStorageSettings.SectionName).Exists())
            {
                services.AddSqlDownloadTokenStorage();
            }
            else if (configuration.GetSection(InMemoryContentStorageSettings.SectionName).Exists())
            {
                services.AddInMemoryDownloadTokenStorage();
            }
            else
            {
                throw new InvalidOperationException("No valid download token storage configuration found.");
            }

            return services;
        }

        private IServiceCollection AddSqlDownloadTokenStorage()
        {
            services.AddSingleton<IDownloadTokenService, SqlDownloadTokenService>();

            return services;
        }

        private IServiceCollection AddInMemoryDownloadTokenStorage()
        {
            services.AddSingleton<IDownloadTokenService, InMemoryDownloadTokenService>();

            return services;
        }
    }
}