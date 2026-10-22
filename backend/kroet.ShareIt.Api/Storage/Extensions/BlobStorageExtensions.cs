using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Storage.InMemory;
using kroet.ShareIt.Api.Storage.S3;

namespace kroet.ShareIt.Api.Storage.Extensions;

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
            else if (configuration.GetSection(InMemoryBlobStorageSettings.SectionName).Exists())
            {
                services.AddInMemoryBlobStorage();
            }
            else
            {
                throw new InvalidOperationException("No valid blob storage configuration found.");
            }

            return services;
        }

        private IServiceCollection AddS3BlobStorage()
        {
            // S3 configuration
            services
                .AddOptions<S3BlobStorageSettings>()
                .BindConfiguration(S3BlobStorageSettings.SectionName)
                .ValidateWithFluentValidator<S3BlobStorageSettings, S3BlobStorageSettingsValidator>()
                .ValidateOnStart();

            services.AddSingleton<IBlobStorageHandler, S3BlobStorageHandler>();

            return services;
        }

        private IServiceCollection AddInMemoryBlobStorage()
        {
            // In-memory blob storage for testing or development
            services.AddSingleton<IBlobStorageHandler, InMemoryBlobStorageHandler>();
            
            return services;
        }
    }
}