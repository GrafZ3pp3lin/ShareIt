using kroet.ShareIt.Api.Configuration;

namespace kroet.ShareIt.Api.Storage.InMemory;

public class InMemoryContentStorageSettings : ISettings
{
    public static string SectionName => "ContentStorage:InMemory";
}