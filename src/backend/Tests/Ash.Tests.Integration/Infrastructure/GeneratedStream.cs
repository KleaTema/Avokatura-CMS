using System.Security.Cryptography;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// A non-seekable stream of deterministic bytes (its length is unknown to the reader), optionally starting with
/// <paramref name="prefix"/> (e.g. "%PDF-"); it hashes what it hands out and records the peak managed memory while
/// being read. Optionally fails after some bytes (a broken upload). For the large-upload tests (tasks 032, 033).
/// </summary>
public sealed class GeneratedStream(long length, long failAfter = -1, byte[]? prefix = null, int seed = 42, long liveSampleEvery = 0) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly Random _random = new(seed);
    private readonly byte[] _prefix = prefix ?? [];
    private long _position;
    private readonly long _liveSampleEvery = liveSampleEvery;
    private long _nextLiveSample = liveSampleEvery;

    /// <summary>The managed heap including garbage not collected yet (allocation churn shows here too).</summary>
    public long PeakManagedMemory { get; private set; }

    /// <summary>
    /// With <c>liveSampleEvery</c>: the heap after a full collection, sampled every that many bytes — what is really
    /// held. A file kept in memory as a whole would show here.
    /// </summary>
    public long PeakLiveMemory { get; private set; }

    public byte[] Sha256 => _hash.GetCurrentHash();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (failAfter >= 0 && _position >= failAfter)
        {
            throw new IOException("The upload source broke off.");
        }

        var count = (int)Math.Min(buffer.Length, length - _position);
        if (count <= 0)
        {
            return 0;
        }

        var chunk = buffer[..count];
        _random.NextBytes(chunk);
        // the prefix overwrites the first bytes of the stream
        for (var i = 0; i < count && _position + i < _prefix.Length; i++)
        {
            chunk[i] = _prefix[_position + i];
        }

        _hash.AppendData(chunk);
        _position += count;
        PeakManagedMemory = Math.Max(PeakManagedMemory, GC.GetTotalMemory(forceFullCollection: false));
        if (_liveSampleEvery > 0 && _position >= _nextLiveSample)
        {
            _nextLiveSample += _liveSampleEvery;
            PeakLiveMemory = Math.Max(PeakLiveMemory, GC.GetTotalMemory(forceFullCollection: true));
        }

        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
