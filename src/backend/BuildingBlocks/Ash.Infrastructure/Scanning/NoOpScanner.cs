namespace Ash.Infrastructure.Scanning;

/// <summary>
/// Development only: every file is CLEAN, without reading it. <see cref="ScanningServiceCollectionExtensions.AddAshScanning"/>
/// refuses to start with it anywhere else (like the dev authentication).
/// </summary>
public sealed class NoOpScanner : IVirusScanner
{
    public Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default) => Task.FromResult(ScanResult.Clean);
}
