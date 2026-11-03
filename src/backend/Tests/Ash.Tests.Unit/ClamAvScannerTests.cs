using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ash.Infrastructure.Scanning;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>Task 033 — the clamd INSTREAM client against a fake clamd on a local port.</summary>
public class ClamAvScannerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("stream: OK\0", true, null)]
    [InlineData("stream: Eicar-Test-Signature FOUND\0", false, "Eicar-Test-Signature")]
    [InlineData("stream: Win.Test.EICAR_HDB-1 FOUND\n", false, "Win.Test.EICAR_HDB-1")]
    public void Parses_the_verdict(string reply, bool clean, string? threat)
    {
        var result = ClamAvScanner.Parse(reply);
        result.IsClean.ShouldBe(clean);
        result.Threat.ShouldBe(threat);
    }

    [Fact]
    public void A_clamd_error_is_unavailable_not_clean()
    {
        Should.Throw<ScannerUnavailableException>(() => ClamAvScanner.Parse("INSTREAM size limit exceeded. ERROR\0"));
    }

    [Fact]
    public async Task Streams_the_file_in_length_prefixed_chunks_and_reads_the_verdict()
    {
        var content = new byte[150_000];
        new Random(3).NextBytes(content);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = FakeClamdAsync(listener, "stream: Eicar-Test-Signature FOUND\0");
        var scanner = new ClamAvScanner(new ClamAvOptions { Host = "127.0.0.1", Port = port, ChunkSize = 64 * 1024, TimeoutSeconds = 10 });

        var result = await scanner.ScanAsync(new MemoryStream(content), Ct);

        result.ShouldBe(ScanResult.Infected("Eicar-Test-Signature"));
        var (command, received, chunks) = await server;
        command.ShouldBe("zINSTREAM\0");
        received.ShouldBe(content);
        chunks.ShouldBe(3); // 64 KB + 64 KB + the rest, then the zero-length chunk
    }

    [Fact]
    public async Task An_unreachable_clamd_is_unavailable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // nothing listens on the port any more
        var scanner = new ClamAvScanner(new ClamAvOptions { Host = "127.0.0.1", Port = port, TimeoutSeconds = 5 });

        await Should.ThrowAsync<ScannerUnavailableException>(() => scanner.ScanAsync(new MemoryStream([1, 2, 3]), Ct));
    }

    [Fact]
    public async Task A_clamd_that_never_answers_times_out_as_unavailable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var silent = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(Ct);
            await Task.Delay(TimeSpan.FromSeconds(10), Ct); // accepts, reads nothing back, never replies
        }, Ct);
        var scanner = new ClamAvScanner(new ClamAvOptions { Host = "127.0.0.1", Port = port, TimeoutSeconds = 1 });

        var error = await Should.ThrowAsync<ScannerUnavailableException>(() => scanner.ScanAsync(new MemoryStream(new byte[10]), Ct));

        error.Message.ShouldContain("did not answer within 1 s");
    }

    /// <summary>One INSTREAM session: the command, the chunks (until the zero-length one), then the reply.</summary>
    private static async Task<(string Command, byte[] Received, int Chunks)> FakeClamdAsync(TcpListener listener, string reply)
    {
        using var client = await listener.AcceptTcpClientAsync(Ct);
        await using var stream = client.GetStream();
        var command = new byte[10];
        await stream.ReadExactlyAsync(command, Ct);
        using var received = new MemoryStream();
        var chunks = 0;
        var length = new byte[4];
        while (true)
        {
            await stream.ReadExactlyAsync(length, Ct);
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(length);
            if (size == 0)
            {
                break;
            }

            var chunk = new byte[size];
            await stream.ReadExactlyAsync(chunk, Ct);
            received.Write(chunk);
            chunks++;
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), Ct);
        return (Encoding.ASCII.GetString(command), received.ToArray(), chunks);
    }
}
