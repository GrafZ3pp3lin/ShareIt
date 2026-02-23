using kroet.ShareIt.Api.Configuration;

namespace kroet.ShareIt.Api.Storage.InMemory;

public class InMemoryBlobStorageSettings : ISettings
{
    public static string SectionName => "BlobStorage:InMemory";
}