namespace Ash.Infrastructure.Scanning;

/// <summary>
/// Antivirus check of an uploaded file (task 033), streamed — the file is never held in memory as a whole.
/// TODO(akshi-q-av): the antivirus product and endpoint at AKSHI (ClamAV clamd is implemented).
/// </summary>
public interface IVirusScanner
{
    /// <summary>The verdict, or <see cref="ScannerUnavailableException"/> when the scanner cannot be reached or times out.</summary>
    Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default);
}

/// <summary>Clean, or infected with the scanner's name for what it found (e.g. "Eicar-Test-Signature").</summary>
public sealed record ScanResult(bool IsClean, string? Threat)
{
    public static ScanResult Clean { get; } = new(true, null);

    public static ScanResult Infected(string threat) => new(false, threat);
}

/// <summary>The scanner did not answer (down, timeout, protocol error): the file stays PENDING and is tried again.</summary>
public sealed class ScannerUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
