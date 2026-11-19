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

        await using var downloaded = await _handler.DownloadAsync(blobId);
        using var buffer = new MemoryStream();
        await downloaded.CopyToAsync(buffer);
        Assert.AreSequenceEqual(content, buffer.ToArray());
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

        await _handler.DeleteAsync(blobId);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => _handler.DownloadAsync(blobId));
    }

    [TestMethod]
    public async Task DeleteAsync_WhenBlobDoesNotExist_Succeeds()
    {
        // Deletion is idempotent: a missing object already satisfies the goal.
        await _handler.DeleteAsync(Guid.NewGuid().ToString());
        await _handler.DeleteAsync("not-a-guid");
    }
}
