using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Content.Configuration;
using kroet.ShareIt.Api.Content.Storage.Blobs;

namespace kroet.ShareIt.Api.Content.Extensions;

public static class BlobStorageExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds blob storage services to the service collection.
        /// </summary>
        public IServiceCollection AddBlobStorage(IConfiguration configuration)
        {
            if (configuration.GetSection(S3BlobStorageSettings.SectionName).Exists())
            {
                services.AddS3BlobStorage();
            }
            else if (configuration.GetSection(MongoBlobStorageSettings.SectionName).Exists())
            {
                services.AddMongoBlobStorage();
            }
            else 
            {
                throw new InvalidOperationException("No valid blob storage configuration found.");
            }

            return services;
        }

        /// <summary>
        /// Adds blob storage services to the service collection.
        /// </summary>
        private IServiceCollection AddMongoBlobStorage()
        {
            // MongoDB configuration
            services.AddOptions<MongoBlobStorageSettings>()
                .BindConfiguration(MongoBlobStorageSettings.SectionName)
                .ValidateWithFluentValidator<MongoBlobStorageSettings, MongoBlobStorageSettingsValidator>()
                .ValidateOnStart();

            services.AddSingleton<IBlobStorageHandler, MongoBlobStorageHandler>();

            return services;
        }

        private IServiceCollection AddS3BlobStorage()
        {
            // S3 configuration
            services.AddOptions<S3BlobStorageSettings>()
                .BindConfiguration(S3BlobStorageSettings.SectionName)
                .ValidateWithFluentValidator<S3BlobStorageSettings, S3BlobStorageSettingsValidator>()
                .ValidateOnStart();

            services.AddSingleton<IBlobStorageHandler, S3BlobStorageHandler>();

            return services;
        }
    }
}