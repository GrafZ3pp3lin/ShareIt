using kroet.ShareIt.Api.Storage.S3;

namespace kroet.ShareIt.Api.Test.Storage.S3;

[TestClass]
public class S3BlobStorageSettingsValidatorTests
{
    [TestMethod]
    public void Validate_WithValidSettings_ReturnsNoErrors()
    {
        var result = new S3BlobStorageSettingsValidator().Validate(CreateSettings());

        Assert.IsTrue(result.IsValid);
        Assert.IsEmpty(result.Errors);
    }

    [TestMethod]
    [DataRow(nameof(S3BlobStorageSettings.AccessKeyId))]
    [DataRow(nameof(S3BlobStorageSettings.SecretAccessKey))]
    [DataRow(nameof(S3BlobStorageSettings.ServiceUrl))]
    [DataRow(nameof(S3BlobStorageSettings.BucketName))]
    public void Validate_WhenRequiredSettingIsEmpty_ReturnsErrorForThatSetting(string propertyName)
    {
        var settings = propertyName switch
        {
            nameof(S3BlobStorageSettings.AccessKeyId) => CreateSettings(accessKeyId: ""),
            nameof(S3BlobStorageSettings.SecretAccessKey) => CreateSettings(secretAccessKey: ""),
            nameof(S3BlobStorageSettings.ServiceUrl) => CreateSettings(serviceUrl: ""),
            nameof(S3BlobStorageSettings.BucketName) => CreateSettings(bucketName: ""),
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "Unknown setting")
        };

        var result = new S3BlobStorageSettingsValidator().Validate(settings);

        Assert.IsFalse(result.IsValid);
        Assert.HasCount(1, result.Errors);
        Assert.AreEqual(propertyName, result.Errors[0].PropertyName);
    }

    [TestMethod]
    public void Validate_WhenAllRequiredSettingsAreEmpty_ReturnsErrorForEachSetting()
    {
        var settings = CreateSettings(accessKeyId: "", secretAccessKey: "", serviceUrl: "", bucketName: "");

        var result = new S3BlobStorageSettingsValidator().Validate(settings);

        Assert.IsFalse(result.IsValid);
        Assert.AreSequenceEqual(
            [
                nameof(S3BlobStorageSettings.AccessKeyId),
                nameof(S3BlobStorageSettings.SecretAccessKey),
                nameof(S3BlobStorageSettings.ServiceUrl),
                nameof(S3BlobStorageSettings.BucketName)
            ], result.Errors.Select(error => error.PropertyName), SequenceOrder.InAnyOrder);
    }

    private static S3BlobStorageSettings CreateSettings(
        string accessKeyId = "access-key",
        string secretAccessKey = "secret-key",
        string serviceUrl = "http://localhost:9000",
        string bucketName = "content-files")
    {
        return new S3BlobStorageSettings
        {
            AccessKeyId = accessKeyId,
            SecretAccessKey = secretAccessKey,
            ServiceUrl = serviceUrl,
            BucketName = bucketName
        };
    }
}
