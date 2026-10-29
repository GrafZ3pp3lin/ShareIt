using System.Text;
using kroet.ShareIt.Api.Storage.InMemory;

namespace kroet.ShareIt.Api.Test.Storage.InMemory;

[TestClass]
public class InMemoryBlobStorageHandlerTests
{
    private readonly InMemoryBlobStorageHandler _handler = new();

    [TestMethod]
    public async Task UploadAsync_ThenDownloadAsync_RoundTripsContent()
    {
        var content = Encoding.UTF8.GetBytes("hello in-memory");
        await using var stream = new MemoryStream(content);

        var blobId = await _handler.UploadAsync(stream, "text/plain", "hello.txt");

        Assert.IsTrue(Guid.TryParse(blobId, out _));
        Assert.IsTrue(await _handler.ExistsAsync(blobId));

        await using var downloaded = await _handler.DownloadAsync(blobId);
        using var buffer = new MemoryStream();
        await downloaded.CopyToAsync(buffer);
        CollectionAssert.AreEqual(content, buffer.ToArray());
    }

    [TestMethod]
    public async Task DownloadAsync_WhenBlobDoesNotExist_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => _handler.DownloadAsync(Guid.NewGuid().ToString()));

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => _handler.DownloadAsync("not-a-guid"));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenBlobExists_RemovesIt()
    {
        await using var stream = new MemoryStream([1, 2, 3]);
        var blobId = await _handler.UploadAsync(stream, "application/octet-stream", "file.bin");

        var deleted = await _handler.DeleteAsync(blobId);

        Assert.IsTrue(deleted);
        Assert.IsFalse(await _handler.ExistsAsync(blobId));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenBlobDoesNotExist_ReturnsFalse()
    {
        Assert.IsFalse(await _handler.DeleteAsync(Guid.NewGuid().ToString()));
        Assert.IsFalse(await _handler.DeleteAsync("not-a-guid"));
    }

    [TestMethod]
    public async Task ExistsAsync_WhenBlobDoesNotExist_ReturnsFalse()
    {
        Assert.IsFalse(await _handler.ExistsAsync(Guid.NewGuid().ToString()));
        Assert.IsFalse(await _handler.ExistsAsync("not-a-guid"));
    }
}
