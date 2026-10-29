using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Storage.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.Test.Storage.InMemory;

[TestClass]
public class InMemoryDownloadTokenServiceTests
{
    private static readonly DateTimeOffset StartTime = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00");

    private readonly TestTimeProvider _timeProvider = new(StartTime);

    private InMemoryDownloadTokenService CreateService()
    {
        return new InMemoryDownloadTokenService(
            Options.Create(new DownloadSettings
            {
                DefaultTokenExpirationMinutes = 15,
                UsedTokenRetentionHours = 24
            }),
            _timeProvider,
            NullLogger<InMemoryDownloadTokenService>.Instance);
    }

    [TestMethod]
    public async Task GenerateTokenAsync_ReturnsTokenWithExpectedMetadata()
    {
        var subject = CreateService();
        var contentId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        var token = await subject.GenerateTokenAsync(contentId, clientId);

        Assert.AreNotEqual(Guid.Empty, token.Id);
        Assert.AreEqual(contentId, token.ContentId);
        Assert.AreEqual(clientId, token.ClientId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.Token));
        Assert.AreEqual(StartTime, token.CreatedAt);
        Assert.AreEqual(StartTime.AddMinutes(15), token.ExpiresAt);
    }

    [TestMethod]
    public async Task ValidateAndConsumeTokenAsync_HandlesUnknownWrongClientAndSingleUse()
    {
        var subject = CreateService();
        var contentId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var token = await subject.GenerateTokenAsync(contentId, clientId);

        var unknown = await subject.ValidateAndConsumeTokenAsync("missing-token", clientId);
        var wrongClient = await subject.ValidateAndConsumeTokenAsync(token.Token, Guid.NewGuid());
        var valid = await subject.ValidateAndConsumeTokenAsync(token.Token, clientId);
        var reused = await subject.ValidateAndConsumeTokenAsync(token.Token, clientId);

        Assert.IsFalse(unknown.IsValid);
        Assert.AreEqual("Token not found", unknown.ErrorMessage);
        Assert.IsFalse(wrongClient.IsValid);
        Assert.AreEqual("Token does not belong to the client", wrongClient.ErrorMessage);
        Assert.IsTrue(valid.IsValid);
        Assert.AreEqual(contentId, valid.ContentId);
        Assert.IsFalse(reused.IsValid);
        Assert.AreEqual("Token not found", reused.ErrorMessage);
    }

    [TestMethod]
    public async Task ValidateAndConsumeTokenAsync_WhenTokenIsExpired_ReturnsInvalid()
    {
        var subject = CreateService();
        var token = await subject.GenerateTokenAsync(Guid.NewGuid(), Guid.NewGuid());

        _timeProvider.UtcNow = token.ExpiresAt.AddSeconds(1);

        var result = await subject.ValidateAndConsumeTokenAsync(token.Token, token.ClientId);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Token has expired", result.ErrorMessage);
    }

    [TestMethod]
    public async Task CleanupExpiredTokensAsync_RemovesOnlyExpiredTokens()
    {
        var subject = CreateService();
        var firstContentId = Guid.NewGuid();
        var secondContentId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        var firstToken = await subject.GenerateTokenAsync(firstContentId, clientId);
        _timeProvider.UtcNow = StartTime.AddMinutes(10);
        var secondToken = await subject.GenerateTokenAsync(secondContentId, clientId);

        _timeProvider.UtcNow = StartTime.AddMinutes(16);
        var deleted = await subject.CleanupExpiredTokensAsync();

        Assert.AreEqual(1, deleted);

        var expired = await subject.ValidateAndConsumeTokenAsync(firstToken.Token, clientId);
        var stillValid = await subject.ValidateAndConsumeTokenAsync(secondToken.Token, clientId);

        Assert.IsFalse(expired.IsValid);
        Assert.AreEqual("Token not found", expired.ErrorMessage);
        Assert.IsTrue(stillValid.IsValid);
        Assert.AreEqual(secondContentId, stillValid.ContentId);
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
