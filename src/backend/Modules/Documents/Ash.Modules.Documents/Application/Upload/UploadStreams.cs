namespace Ash.Modules.Documents.Application.Upload;

/// <summary>
/// The upload as storage reads it: the bytes already peeked for the magic check, then the rest of the request
/// section, counted; reading past <c>maxBytes</c> throws <see cref="FileTooLargeException"/> at once (the multipart
/// upload is aborted), so an oversized file is never stored and never read to its end.
/// </summary>
internal sealed class LimitedUploadStream(byte[] prefix, Stream rest, long maxBytes) : Stream
{
    private int _prefixPosition;
    private long _count;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;
        if (_prefixPosition < prefix.Length)
        {
            read = Math.Min(buffer.Length, prefix.Length - _prefixPosition);
            prefix.AsMemory(_prefixPosition, read).CopyTo(buffer);
            _prefixPosition += read;
        }
        else
        {
            read = await rest.ReadAsync(buffer, cancellationToken);
        }

        _count += read;
        if (_count > maxBytes)
        {
            throw new FileTooLargeException();
        }

        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class FileTooLargeException() : Exception("The upload exceeds the size limit of its file type.");
