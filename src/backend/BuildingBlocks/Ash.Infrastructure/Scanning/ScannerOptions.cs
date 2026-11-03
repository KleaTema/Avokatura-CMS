namespace Ash.Infrastructure.Scanning;

/// <summary>Configuration section "Scanner".</summary>
public sealed class ScannerOptions
{
    public const string Section = "Scanner";

    /// <summary>NoOp (Development only: every file is CLEAN) or ClamAv. Default: NoOp in Development, ClamAv elsewhere.</summary>
    public ScannerMode? Mode { get; set; }

    public ClamAvOptions ClamAv { get; set; } = new();

    public ScanJobOptions Job { get; set; } = new();
}

public enum ScannerMode
{
    NoOp,
    ClamAv
}

public sealed class ClamAvOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 3310;

    /// <summary>For connecting and for the whole scan of one file.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>The INSTREAM chunk size (clamd reads up to its StreamMaxLength in total).</summary>
    public int ChunkSize { get; set; } = 64 * 1024;
}

/// <summary>The background job that scans PENDING uploads.</summary>
public sealed class ScanJobOptions
{
    /// <summary>Off in tests (they run the job by hand); on everywhere else.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 5;

    /// <summary>At most this many documents per run.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>After this many failed attempts (scanner down) the document becomes FAILED.</summary>
    public int MaxAttempts { get; set; } = 5;
}
