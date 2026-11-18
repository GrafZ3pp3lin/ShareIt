using System.Buffers;

namespace kroet.ShareIt.Api.Content;

/// <summary>
/// Raised when a request body exceeds the per-identity upload limit.
/// </summary>
internal sealed class ContentTooLargeException()
    : Exception("The uploaded content exceeds the per-identity upload limit.");

/// <summary>
/// Raised when a request body ends before or after the declared content size.
/// </summary>
internal sealed class ContentLengthMismatchException()
    : Exception("The uploaded content length does not match the declared size.");

/// <summary>
/// Raised when the multipart request body ends before the final boundary.
/// Callers can treat it as a client error without also catching storage I/O
/// failures.
/// </summary>
internal sealed class RequestBodyTruncatedException(Exception innerException)
    : Exception("The request body ended unexpectedly.", innerException);

/// <summary>
/// Read-only view over the file part of a multipart upload that counts the
/// bytes actually read, rejects anything above the per-identity limit before it
/// reaches storage, and verifies at end-of-stream that the client sent exactly
/// the size it declared. Reading never buffers more than the caller's buffer.
/// </summary>
internal sealed class DeclaredLengthStream(Stream inner, long limitBytes, long declaredBytes) : Stream
{
    private long _bytesRead;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        int read;
        try
        {
            read = inner.Read(buffer);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new RequestBodyTruncatedException(ex);
        }

        Track(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        int read;
        try
        {
            read = await inner.ReadAsync(buffer, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new RequestBodyTruncatedException(ex);
        }

        Track(read);
        return read;
    }

    /// <summary>
    /// Copies the body through to <paramref name="destination"/> while keeping
    /// the same counting rules. Uses a pooled buffer so a caller that does not
    /// stream explicitly still gets bounded memory use.
    /// </summary>
    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            while (true)
            {
                var read = await ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Continues reading the request body after a declared-size mismatch, up to
    /// the identity limit, so the endpoint can preserve 413 precedence without
    /// sending any more bytes to storage. Returns true when the limit is
    /// exceeded.
    /// </summary>
    public async Task<bool> DrainToLimitAsync(CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            while (_bytesRead <= limitBytes)
            {
                int read;
                try
                {
                    read = await inner.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    throw new RequestBodyTruncatedException(ex);
                }

                if (read == 0)
                {
                    return false;
                }

                _bytesRead += read;
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Track(int read)
    {
        if (read == 0)
        {
            // The uploader always reads until the stream reports end-of-stream,
            // so this is the only place that can compare declared and actual.
            if (_bytesRead != declaredBytes)
            {
                throw new ContentLengthMismatchException();
            }

            return;
        }

        _bytesRead += read;

        // Keep the absolute limit as the stronger signal when a single read
        // crosses both bounds.
        if (_bytesRead > limitBytes)
        {
            throw new ContentTooLargeException();
        }

        if (_bytesRead > declaredBytes)
        {
            // Stop forwarding bytes to storage as soon as the client exceeds
            // its declaration. The endpoint drains the rest separately only to
            // decide whether this is a 400 or a 413.
            throw new ContentLengthMismatchException();
        }
    }
}
