using System.Reflection;
using kroet.ShareIt.Api.Services.Cleanup;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace kroet.ShareIt.Api.Test.Services.Cleanup;

[TestClass]
public class CleanupServiceTests
{
    private readonly IDownloadTokenService _tokenService = Substitute.For<IDownloadTokenService>();
    private readonly IContentStorageHandler _contentStorageHandler = Substitute.For<IContentStorageHandler>();
    private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceProvider _scopeProvider = Substitute.For<IServiceProvider>();
    private readonly CleanupService _subject;

    public CleanupServiceTests()
    {
        _scopeProvider.GetService(typeof(IDownloadTokenService)).Returns(_tokenService);
        _scopeProvider.GetService(typeof(IContentStorageHandler)).Returns(_contentStorageHandler);
        _scope.ServiceProvider.Returns(_scopeProvider);
        _scopeFactory.CreateScope().Returns(_scope);

        _subject = new CleanupService(
            _scopeFactory,
            Options.Create(new CleanupSettings { CleanupIntervalMinutes = 1 }),
            NullLogger<CleanupService>.Instance);
    }

    [TestMethod]
    public async Task RunCleanupAsync_DeletesExpiredTokensAndPackages()
    {
        var firstPackageId = Guid.NewGuid();
        var secondPackageId = Guid.NewGuid();
        _tokenService.CleanupExpiredTokensAsync(Arg.Any<CancellationToken>()).Returns(2);
        _contentStorageHandler.GetPackagesToDeleteAsync(Arg.Any<CancellationToken>())
            .Returns([firstPackageId, secondPackageId]);
        _contentStorageHandler.DeletePackagesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<CancellationToken>()).Returns(1);

        await InvokeRunCleanupAsync();

        await _tokenService.Received(1).CleanupExpiredTokensAsync(Arg.Any<CancellationToken>());
        await _contentStorageHandler.Received(1).DeletePackagesAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { firstPackageId, secondPackageId })),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunCleanupAsync_WhenPackageDeletionFails_DoesNotThrow()
    {
        _tokenService.CleanupExpiredTokensAsync(Arg.Any<CancellationToken>()).Returns(1);
        _contentStorageHandler.GetPackagesToDeleteAsync(Arg.Any<CancellationToken>())
            .Returns([Guid.NewGuid()]);
        _contentStorageHandler.DeletePackagesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<CancellationToken>()).Returns(
                Task.FromException<int>(new InvalidOperationException("delete failed")));

        await InvokeRunCleanupAsync();

        await _tokenService.Received(1).CleanupExpiredTokensAsync(Arg.Any<CancellationToken>());
        await _contentStorageHandler.Received(1).DeletePackagesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCancelled_StopsCleanly()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var executeMethod = typeof(CleanupService).GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)executeMethod.Invoke(_subject, [cancellationTokenSource.Token])!;

        await _tokenService.DidNotReceiveWithAnyArgs().CleanupExpiredTokensAsync(default);
    }

    private async Task InvokeRunCleanupAsync()
    {
        var cleanupMethod = typeof(CleanupService).GetMethod(
            "RunCleanupAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)cleanupMethod.Invoke(_subject, [CancellationToken.None])!;
    }
}
