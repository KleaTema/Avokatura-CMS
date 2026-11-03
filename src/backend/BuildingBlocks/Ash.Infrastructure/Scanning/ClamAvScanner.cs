using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Ash.Infrastructure.Scanning;

/// <summary>
/// ClamAV through clamd's INSTREAM command over TCP: "zINSTREAM\0", then chunks of [4-byte big-endian length][data],
/// a zero-length chunk to finish, and one reply ending in \0 — "stream: OK", "stream: &lt;name&gt; FOUND" or "… ERROR".
/// The file is streamed chunk by chunk; the whole exchange is bounded by the configured timeout.
/// </summary>
public sealed class ClamAvScanner(ClamAvOptions options) : IVirusScanner
{
    public async Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(options.Host, options.Port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);

            var buffer = new byte[4 + options.ChunkSize];
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(4, options.ChunkSize), timeout.Token);
                BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)read);
                await stream.WriteAsync(buffer.AsMemory(0, 4 + read), timeout.Token);
                if (read == 0)
                {
                    break; // the zero-length chunk ends the stream
                }
            }

            var reply = await ReadReplyAsync(stream, timeout.Token);
            return Parse(reply);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ScannerUnavailableException($"ClamAV at {options.Host}:{options.Port} did not answer within {options.TimeoutSeconds} s.");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            throw new ScannerUnavailableException($"ClamAV at {options.Host}:{options.Port} is not reachable.", ex);
        }
    }

    /// <summary>"stream: OK" → clean; "stream: X FOUND" → infected with X; anything else (size limit, error) → unavailable.</summary>
    public static ScanResult Parse(string reply)
    {
        var text = reply.Trim('\0', ' ', '\n');
        const string prefix = "stream: ";
        var body = text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;
        if (body == "OK")
        {
            return ScanResult.Clean;
        }

        if (body.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            return ScanResult.Infected(body[..^" FOUND".Length]);
        }

        throw new ScannerUnavailableException($"ClamAV could not scan the file: {text}");
    }

    private static async Task<string> ReadReplyAsync(NetworkStream stream, CancellationToken ct)
    {
        var reply = new StringBuilder();
        var buffer = new byte[256];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                break;
            }

            reply.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (buffer[read - 1] == 0)
            {
                break;
            }
        }

        return reply.ToString();
    }
}
