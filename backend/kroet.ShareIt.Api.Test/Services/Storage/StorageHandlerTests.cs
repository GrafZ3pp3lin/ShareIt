using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ContentModel = kroet.ShareIt.Api.Models.Content;

namespace kroet.ShareIt.Api.Test.Services.Storage;

[TestClass]
public class StorageHandlerTests
{
    private readonly IContentStorageHandler _contentStorageHandler = Substitute.For<IContentStorageHandler>();
    private readonly IBlobStorageHandler _blobStorageHandler = Substitute.For<IBlobStorageHandler>();
    private readonly ILogger<StorageHandler> _logger = Substitute.For<ILogger<StorageHandler>>();

    private StorageHandler CreateSubject()
    {
        _contentStorageHandler
            .GetPendingBlobDeletionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        return new StorageHandler(_contentStorageHandler, _blobStorageHandler, _logger);
    }

    [TestMethod]
    public async Task StoreContentAsync_UploadsBlobAndStoresContentWithUrl()
    {
        var subject = CreateSubject();
        var content = CreateContent();
        await using var stream = new MemoryStream([1, 2, 3]);

        _blobStorageHandler
            .UploadAsync(stream, "application/octet-stream", content.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns("blob://content");

        await subject.StoreContentAsync(content, stream);

        await _contentStorageHandler.Received(1).StoreContentAsync(
            Arg.Is<ContentModel>(stored =>
                stored.Id == content.Id &&
                stored.PackageId == content.PackageId &&
                stored.ContentType == content.ContentType &&
                stored.Size == content.Size &&
                stored.Meta == content.Meta &&
                stored.Url == "blob://content"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StoreContentAsync_DeletesBlobWhenMetadataStorageFails()
    {
        var subject = CreateSubject();
        var content = CreateContent();
        await using var stream = new MemoryStream([1, 2, 3]);

        _blobStorageHandler
            .UploadAsync(stream, "application/octet-stream", content.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns("blob://content");
        _contentStorageHandler
            .StoreContentAsync(Arg.Any<ContentModel>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("storage failed"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => subject.StoreContentAsync(content, stream));

        await _blobStorageHandler.Received(1).DeleteAsync("blob://content", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StoreContentAsync_RethrowsOriginalWhenBlobCleanupAlsoFails()
    {
        var subject = CreateSubject();
        var content = CreateContent();
        await using var stream = new MemoryStream([1, 2, 3]);

        _blobStorageHandler
            .UploadAsync(stream, "application/octet-stream", content.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns("blob://content");
        _contentStorageHandler
            .StoreContentAsync(Arg.Any<ContentModel>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("metadata failed"));
        _blobStorageHandler
            .DeleteAsync("blob://content", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cleanup failed")));

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => subject.StoreContentAsync(content, stream));

        Assert.AreEqual("metadata failed", exception.Message);
        await _blobStorageHandler.Received(1).DeleteAsync("blob://content", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StoreContentAsync_WhenRequestIsCanceled_CompensatesWithAnIndependentToken()
    {
        var subject = CreateSubject();
        var content = CreateContent();
        await using var stream = new MemoryStream([1, 2, 3]);
        using var requestCancellation = new CancellationTokenSource();
        await requestCancellation.CancelAsync();
        var cleanupToken = CancellationToken.None;

        _blobStorageHandler
            .UploadAsync(stream, "application/octet-stream", content.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns("blob://content");
        _contentStorageHandler
            .StoreContentAsync(Arg.Any<ContentModel>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new OperationCanceledException(requestCancellation.Token));
        _blobStorageHandler
            .DeleteAsync("blob://content", Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cleanupToken = call.Arg<CancellationToken>();
                return Task.CompletedTask;
            });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => subject.StoreContentAsync(content, stream, requestCancellation.Token));

        Assert.IsFalse(cleanupToken.IsCancellationRequested);
        await _blobStorageHandler.Received(1).DeleteAsync("blob://content", cleanupToken);
    }

    [TestMethod]
    public async Task DeletePackageAsync_DeletesMetadataWithoutDrainingQueue()
    {
        var subject = CreateSubject();
        var packageId = Guid.NewGuid();
        _contentStorageHandler.DeletePackageAsync(packageId, Arg.Any<CancellationToken>()).Returns(true);

        var deleted = await subject.DeletePackageAsync(packageId);

        Assert.IsTrue(deleted);
        await _contentStorageHandler.Received(1).DeletePackageAsync(packageId, Arg.Any<CancellationToken>());
        await _contentStorageHandler.DidNotReceiveWithAnyArgs().GetPendingBlobDeletionsAsync(default, default);
        await _blobStorageHandler.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [TestMethod]
    public async Task DeletePackagesAsync_DeletesMetadataWithoutDrainingQueue()
    {
        var subject = CreateSubject();
        var packageIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _contentStorageHandler
            .DeletePackagesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(2);

        var deleted = await subject.DeletePackagesAsync(packageIds);

        Assert.AreEqual(2, deleted);
        await _contentStorageHandler.Received(1).DeletePackagesAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(packageIds)),
            Arg.Any<CancellationToken>());
        await _contentStorageHandler.DidNotReceiveWithAnyArgs().GetPendingBlobDeletionsAsync(default, default);
        await _blobStorageHandler.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [TestMethod]
    public async Task ProcessPendingBlobDeletionsAsync_WhenBlobDeletionFails_KeepsJobForRetry()
    {
        var subject = CreateSubject();
        _contentStorageHandler
            .GetPendingBlobDeletionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(["blob://unavailable"]);
        _blobStorageHandler
            .DeleteAsync("blob://unavailable", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("S3 unavailable")));

        var completed = await subject.ProcessPendingBlobDeletionsAsync();

        Assert.AreEqual(0, completed);
        await _contentStorageHandler.DidNotReceiveWithAnyArgs().CompleteBlobDeletionAsync(default!, default);
    }

    [TestMethod]
    public async Task ProcessPendingBlobDeletionsAsync_WhenBlobIsAlreadyMissing_CompletesJob()
    {
        var subject = CreateSubject();
        _contentStorageHandler
            .GetPendingBlobDeletionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(["blob://already-missing"]);

        var completed = await subject.ProcessPendingBlobDeletionsAsync();

        Assert.AreEqual(1, completed);
        await _contentStorageHandler.Received(1).CompleteBlobDeletionAsync("blob://already-missing", Arg.Any<CancellationToken>());
    }

    private static ContentModel CreateContent(Guid? contentId = null)
    {
        return new ContentModel
        {
            Id = contentId ?? Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ContentType = ContentType.File,
            CreatedAt = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00"),
            Size = 3,
            UserId = Guid.NewGuid(),
            Meta = "bWV0YQ"
        };
    }
}
