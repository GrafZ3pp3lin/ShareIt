using kroet.ShareIt.Api.Configuration;

namespace kroet.ShareIt.Api.Storage.Extensions;

public static class AccessSessionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers access-session settings and the session service. The
        /// service uses <see cref="IContentStorageHandler"/> for the atomic
        /// access claim plus session insert.
        /// </summary>
        public IServiceCollection AddAccessSessionStorage()
        {
            services.AddOptions<DownloadSettings>()
                .BindConfiguration(DownloadSettings.SectionName)
                .ValidateWithFluentValidator<DownloadSettings, DownloadSettingsValidator>()
                .ValidateOnStart();

            services.AddSingleton<IAccessSessionService, AccessSessionService>();

            return services;
        }
    }
}
