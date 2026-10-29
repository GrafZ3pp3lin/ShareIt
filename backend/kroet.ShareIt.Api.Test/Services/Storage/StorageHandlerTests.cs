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

    private StorageHandler CreateSubject() => new(_contentStorageHandler, _blobStorageHandler, _logger);

    [TestMethod]
    public async Task StoreContentAsync_UploadsBlobAndStoresExternalContent()
    {
        var subject = CreateSubject();
        var content = CreateContent();
        await using var stream = new MemoryStream([1, 2, 3]);

        _blobStorageHandler
            .UploadAsync(stream, content.MimeType, content.FileName, Arg.Any<CancellationToken>())
            .Returns("blob://content");

        await subject.StoreContentAsync(content, stream);

        await _contentStorageHandler.Received(1).StoreContentAsync(
            Arg.Is<ExternalContent>(stored =>
                stored.Id == content.Id &&
                stored.PackageId == content.PackageId &&
                stored.ContentType == content.ContentType &&
                stored.Size == content.Size &&
                stored.FileName == content.FileName &&
                stored.MimeType == content.MimeType &&
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
            .UploadAsync(stream, content.MimeType, content.FileName, Arg.Any<CancellationToken>())
            .Returns("blob://content");
        _contentStorageHandler
            .StoreContentAsync(Arg.Any<ExternalContent>(), Arg.Any<CancellationToken>())
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
            .UploadAsync(stream, content.MimeType, content.FileName, Arg.Any<CancellationToken>())
            .Returns("blob://content");
        _contentStorageHandler
            .StoreContentAsync(Arg.Any<ExternalContent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("metadata failed"));
        _blobStorageHandler
            .DeleteAsync("blob://content", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("cleanup failed")));

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => subject.StoreContentAsync(content, stream));

        Assert.AreEqual("metadata failed", exception.Message);
        await _blobStorageHandler.Received(1).DeleteAsync("blob://content", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DeleteContentAsync_ReturnsFalseWhenContentDoesNotExist()
    {
        var subject = CreateSubject();
        var contentId = Guid.NewGuid();

        _contentStorageHandler
            .GetContentAsync(contentId, Arg.Any<CancellationToken>())
            .Returns((ExternalContent?)null);

        var deleted = await subject.DeleteContentAsync(contentId);

        Assert.IsFalse(deleted);
        await _blobStorageHandler.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        await _contentStorageHandler.DidNotReceiveWithAnyArgs().DeleteContentAsync(default, default);
    }

    [TestMethod]
    public async Task DeleteContentAsync_ReturnsFalseWhenBlobDeletionFails()
    {
        var subject = CreateSubject();
        var contentId = Guid.NewGuid();
        var externalContent = new ExternalContent(CreateContent(contentId), "blob://content");

        _contentStorageHandler
            .GetContentAsync(contentId, Arg.Any<CancellationToken>())
            .Returns(externalContent);
        _blobStorageHandler
            .DeleteAsync(externalContent.Url, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("blob delete failed")));

        var deleted = await subject.DeleteContentAsync(contentId);

        Assert.IsFalse(deleted);
        await _contentStorageHandler.DidNotReceiveWithAnyArgs().DeleteContentAsync(default, default);
    }

    [TestMethod]
    public async Task RetrieveContentAsync_ThrowsWhenContentDoesNotExist()
    {
        var subject = CreateSubject();
        var contentId = Guid.NewGuid();

        _contentStorageHandler
            .GetContentAsync(contentId, Arg.Any<CancellationToken>())
            .Returns((ExternalContent?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => subject.RetrieveContentAsync(contentId));
    }

    [TestMethod]
    public async Task RetrieveContentAsync_ReturnsDownloadedStream()
    {
        var subject = CreateSubject();
        var contentId = Guid.NewGuid();
        var externalContent = new ExternalContent(CreateContent(contentId), "blob://content");
        var expectedStream = new MemoryStream([1, 2, 3]);

        _contentStorageHandler
            .GetContentAsync(contentId, Arg.Any<CancellationToken>())
            .Returns(externalContent);
        _blobStorageHandler
            .DownloadAsync(externalContent.Url, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(expectedStream));

        var result = await subject.RetrieveContentAsync(contentId);

        Assert.AreSame(expectedStream, result);
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
            MimeType = "text/plain",
            FileName = "test.txt"
        };
    }
}
