using System.Globalization;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using Microsoft.Extensions.Time.Testing;

namespace kroet.ShareIt.Api.Test.Storage;

[TestClass]
public class AccessSessionServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture);
    private static readonly Guid PackageId = Guid.Parse("0192f3ab-7cde-7def-8abc-0123456789ab");
    private static readonly Guid UserId = Guid.Parse("0192f3ab-7cde-7def-8abc-0123456789ac");
    private static readonly string Token = OpaqueToken.Create();

    private readonly IContentStorageHandler _storage = Substitute.For<IContentStorageHandler>();
    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly AccessSessionService _subject;

    public AccessSessionServiceTests()
    {
        _subject = new AccessSessionService(
            _storage,
            Options.Create(new DownloadSettings { AccessSessionLifetimeMinutes = 15 }),
            _timeProvider);
    }

    [TestMethod]
    public async Task CreateAsync_StoresOnlyTheTokenHashAndReturnsTheOpaqueToken()
    {
        AccessSession? stored = null;
        _storage.CreateAccessSessionAsync(Arg.Do<AccessSession>(session => stored = session), Arg.Any<CancellationToken>())
            .Returns(new AccessSessionClaimResult(true, 1, 3));

        var result = await _subject.CreateAsync(PackageId, UserId);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Ticket);
        Assert.IsNotNull(stored);
        Assert.AreNotEqual(result.Ticket.Token, stored.TokenHash);
        Assert.AreEqual(OpaqueToken.Hash(result.Ticket.Token), stored.TokenHash);
        Assert.AreEqual(Now, stored.CreatedAt);
        Assert.AreEqual(Now.AddMinutes(15), stored.ExpiresAt);
        Assert.AreEqual(PackageId, stored.PackageId);
        Assert.AreEqual(UserId, stored.UserId);
        Assert.AreEqual(1, result.NewAccessCount);
        Assert.AreEqual(3, result.MaxAccessCount);
    }

    [TestMethod]
    public async Task CreateAsync_WhenClaimFails_ReturnsNoTicket()
    {
        _storage.CreateAccessSessionAsync(Arg.Any<AccessSession>(), Arg.Any<CancellationToken>())
            .Returns(new AccessSessionClaimResult(false, 0, null));

        var result = await _subject.CreateAsync(PackageId, userId: null);

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Ticket);
    }

    [TestMethod]
    public async Task ValidateAsync_AcceptsOnlyTheMatchingLiveSession()
    {
        var session = NewSession(Now.AddMinutes(15));
        _storage.GetAccessSessionByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(session);

        var valid = await _subject.ValidateAsync(Token, PackageId, UserId);

        Assert.IsNotNull(valid);
        Assert.AreEqual(Token, valid.Token);
        Assert.AreEqual(session.ExpiresAt, valid.ExpiresAt);
        await _storage.Received(1).GetAccessSessionByTokenHashAsync(
            OpaqueToken.Hash(Token),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ValidateAsync_RejectsMalformedTokenWithoutReadingTheStore()
    {
        Assert.IsNull(await _subject.ValidateAsync(string.Empty, PackageId, UserId));
        Assert.IsNull(await _subject.ValidateAsync(" ", PackageId, UserId));
        Assert.IsNull(await _subject.ValidateAsync("token", PackageId, UserId));
        Assert.IsNull(await _subject.ValidateAsync(Token[..^1], PackageId, UserId));
        Assert.IsNull(await _subject.ValidateAsync(Token + "A", PackageId, UserId));
        Assert.IsNull(await _subject.ValidateAsync(Token[..^1] + "=", PackageId, UserId));

        await _storage.DidNotReceiveWithAnyArgs()
            .GetAccessSessionByTokenHashAsync(default!, default);
    }

    [TestMethod]
    public async Task ValidateAsync_RejectsBlankWrongBindingAndExpiredSessions()
    {
        _storage.GetAccessSessionByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(NewSession(Now.AddMinutes(15)));

        Assert.IsNull(await _subject.ValidateAsync(" ", PackageId, UserId));
        Assert.IsNull(await _subject.ValidateAsync(Token, Guid.NewGuid(), UserId));
        Assert.IsNull(await _subject.ValidateAsync(Token, PackageId, Guid.NewGuid()));
        Assert.IsNull(await _subject.ValidateAsync(Token, PackageId, userId: null));
        Assert.IsNotNull(await _subject.ValidateAsync(Token, PackageId, UserId));

        _storage.GetAccessSessionByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(NewSession(Now.AddSeconds(-1)));

        Assert.IsNull(await _subject.ValidateAsync(Token, PackageId, UserId));
    }

    [TestMethod]
    public async Task CleanupExpiredAsync_DelegatesToTheStoreWithTheCurrentTime()
    {
        _storage.DeleteExpiredAccessSessionsAsync(Now, Arg.Any<CancellationToken>()).Returns(2L);

        var deleted = await _subject.CleanupExpiredAsync();

        Assert.AreEqual(2L, deleted);
    }

    private static AccessSession NewSession(DateTimeOffset expiresAt)
        => new()
        {
            Id = Guid.NewGuid(),
            PackageId = PackageId,
            UserId = UserId,
            TokenHash = OpaqueToken.Hash(Token),
            CreatedAt = Now,
            ExpiresAt = expiresAt
        };
}
