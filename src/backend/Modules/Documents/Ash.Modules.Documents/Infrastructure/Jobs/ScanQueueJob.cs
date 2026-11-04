using System.Collections.Concurrent;
using Ash.Infrastructure.Persistence;
using Ash.Infrastructure.Scanning;
using Ash.Infrastructure.Storage;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Documents.Contracts.Events;
using Ash.Modules.Documents.Infrastructure.Scanning;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Documents.Infrastructure.Jobs;

/// <summary>
/// Scans PENDING uploads (task 033). Every few seconds it claims documents ONE AT A TIME, each in its own transaction,
/// with SELECT … FOR UPDATE SKIP LOCKED: several API instances never take the same row, and the lock is held only
/// while that one file is scanned. Then:
/// - CLEAN → copied to the documents bucket (same key), the row says so, COMMIT, then the quarantine copy is deleted;
/// - INFECTED (or an Office file that is not one: "format i pavlefshëm") → the row says so, DOCUMENT_REJECTED, the
///   event DocumentInfected; the file stays in quarantine and expires there;
/// - scanner unavailable / timeout / storage error → stays PENDING and is tried again on a later run; the attempts
///   are counted in memory (per instance) and logged; after MaxAttempts → FAILED and DocumentScanFailed.
/// TODO(akshi-q-av): the antivirus product and endpoint at AKSHI.
/// </summary>
public sealed class ScanQueueJob(
    IServiceScopeFactory scopes,
    IVirusScanner scanner,
    StorageOptions storageOptions,
    ScanJobOptions options,
    ILogger<ScanQueueJob> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<ulong, int> _attempts = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (!storageOptions.IsConfigured)
        {
            logger.LogWarning("The document scan job does not run: storage is not configured.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.IntervalSeconds));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "The document scan job run failed; it runs again at the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One run: up to BatchSize documents, each tried at most once. Returns how many were decided.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var tried = new List<ulong>();
        var decided = 0;
        for (var i = 0; i < options.BatchSize; i++)
        {
            var outcome = await ProcessNextAsync(tried, ct);
            if (outcome == Outcome.NothingPending)
            {
                break;
            }

            if (outcome == Outcome.Decided)
            {
                decided++;
            }
        }

        return decided;
    }

    private enum Outcome
    {
        NothingPending,
        Decided,
        TryLater,
    }

    private async Task<Outcome> ProcessNextAsync(List<ulong> tried, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var db = services.GetRequiredService<DocumentsDbContext>();
        await unitOfWork.BeginAsync(ct);

        var id = await ClaimAsync(unitOfWork, tried, ct);
        if (id is null)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            return Outcome.NothingPending;
        }

        tried.Add(id.Value);
        var row = await db.Documents.SingleAsync(d => d.Id == id.Value, ct);
        string? quarantineKey = null;
        try
        {
            var verdict = await ScanAsync(services.GetRequiredService<IFileStorage>(), row, ct);
            var clock = services.GetRequiredService<IClock>();
            if (verdict is null)
            {
                await MarkFailedAsync(services, row, clock, "the object is missing from the quarantine bucket", ct);
            }
            else if (verdict.Value.Clean)
            {
                quarantineKey = await MarkCleanAsync(services.GetRequiredService<IFileStorage>(), db, row, clock, ct);
                await services.GetRequiredService<IEventPublisher>().PublishAsync(
                    new DocumentScanned(row.PublicId, row.UploadedBy, DocumentRow.Clean), ct);
            }
            else
            {
                await MarkInfectedAsync(services, db, row, clock, verdict.Value.Reason, ct);
            }

            await unitOfWork.CommitAsync(ct);
            _attempts.TryRemove(row.Id, out _);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            return await CountFailedAttemptAsync(row.Id, ex, ct);
        }

        if (quarantineKey is not null)
        {
            await DeleteQuarantineCopyAsync(services.GetRequiredService<IFileStorage>(), quarantineKey);
        }

        return Outcome.Decided;
    }

    /// <summary>The oldest PENDING document not tried in this run, locked for this transaction; others skip it.</summary>
    private static async Task<ulong?> ClaimAsync(IUnitOfWork unitOfWork, List<ulong> tried, CancellationToken ct)
    {
        var except = tried.Count == 0 ? string.Empty : $"AND id NOT IN ({string.Join(',', tried)}) ";
        await using var command = unitOfWork.CreateCommand(
            "SELECT id FROM documents WHERE scan_status = 'PENDING' AND deleted_at IS NULL " + except +
            "ORDER BY uploaded_at, id LIMIT 1 FOR UPDATE SKIP LOCKED");
        return await command.ExecuteScalarAsync(ct) is { } value ? Convert.ToUInt64(value) : null;
    }

    /// <summary>The verdict, or null when the object is gone. An Office file is also checked for a real Office format.</summary>
    private async Task<(bool Clean, string Reason)?> ScanAsync(IFileStorage storage, DocumentRow row, CancellationToken ct)
    {
        await using var file = await storage.OpenReadAsync(row.StorageBucket, row.StorageKey, ct);
        if (file is null)
        {
            return null;
        }

        var extension = Path.GetExtension(row.OriginalFileName).TrimStart('.');
        if (!OfficeFormatCheck.AppliesTo(extension))
        {
            var result = await scanner.ScanAsync(file.Content, ct);
            return result.IsClean ? (true, string.Empty) : (false, $"u gjet virus ({result.Threat})");
        }

        // The zip check needs to seek (the central directory is at the end), and the scan needs the bytes too: an
        // Office file is read once into memory — at most its type's size limit, one file at a time per instance.
        using var copy = new MemoryStream((int)Math.Min(row.SizeBytes, int.MaxValue));
        await file.Content.CopyToAsync(copy, ct);
        copy.Position = 0;
        var scan = await scanner.ScanAsync(copy, ct);
        if (!scan.IsClean)
        {
            return (false, $"u gjet virus ({scan.Threat})");
        }

        copy.Position = 0;
        return OfficeFormatCheck.IsValid(copy, extension) ? (true, string.Empty) : (false, OfficeFormatCheck.InvalidReason);
    }

    /// <summary>
    /// Server-side copy to the documents bucket under the same key, then the row; the quarantine copy is deleted only
    /// after the COMMIT (returned key). A crash in between leaves a quarantine copy that expires on its own.
    /// TODO(akshi-q-storage, client-q9): object lock retention on the copied object (left to the bucket's default).
    /// </summary>
    private async Task<string> MarkCleanAsync(IFileStorage storage, DocumentsDbContext db, DocumentRow row, IClock clock, CancellationToken ct)
    {
        var quarantine = row.StorageBucket;
        await storage.CopyAsync(quarantine, row.StorageKey, storageOptions.Buckets.Documents, row.StorageKey, ct);
        row.StorageBucket = storageOptions.Buckets.Documents;
        row.ScanStatus = DocumentRow.Clean;
        row.ScannedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Document {DocumentId} scanned CLEAN.", row.PublicId);
        return row.StorageKey;
    }

    private async Task MarkInfectedAsync(IServiceProvider services, DocumentsDbContext db, DocumentRow row, IClock clock, string reason, CancellationToken ct)
    {
        row.ScanStatus = DocumentRow.Infected;
        row.ScannedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        var cases = services.GetRequiredService<ICaseActivity>();
        await cases.AddHistoryAsync((long)row.DossierId, (long?)row.CaseId, HistoryEventType.DocumentRejected,
            Summary($"Dokumenti \"{row.Title}\" u refuzua: {reason}."), new { row.Title, Reason = reason }, ct);
        await services.GetRequiredService<IEventPublisher>().PublishAsync(
            new DocumentInfected(row.PublicId, (long)(row.CaseId ?? 0), row.UploadedBy, row.Title, reason), ct);
        logger.LogWarning("Document {DocumentId} rejected: {Reason}.", row.PublicId, reason);
    }

    private async Task MarkFailedAsync(IServiceProvider services, DocumentRow row, IClock clock, string why, CancellationToken ct)
    {
        row.ScanStatus = DocumentRow.Failed;
        row.ScannedAt = clock.UtcNow;
        await services.GetRequiredService<DocumentsDbContext>().SaveChangesAsync(ct);
        await services.GetRequiredService<IEventPublisher>().PublishAsync(
            new DocumentScanFailed(row.PublicId, (long)(row.CaseId ?? 0), row.UploadedBy, row.Title), ct);
        logger.LogError("Document {DocumentId} could not be scanned: {Why}.", row.PublicId, why);
    }

    /// <summary>Stays PENDING for a later run; after MaxAttempts it becomes FAILED (in its own transaction).</summary>
    private async Task<Outcome> CountFailedAttemptAsync(ulong id, Exception error, CancellationToken ct)
    {
        var attempts = _attempts.AddOrUpdate(id, 1, (_, n) => n + 1);
        logger.LogWarning(error, "Scanning document row {Id} failed (attempt {Attempt} of {Max}).", id, attempts, options.MaxAttempts);
        if (attempts < options.MaxAttempts)
        {
            return Outcome.TryLater;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var db = services.GetRequiredService<DocumentsDbContext>();
        await unitOfWork.BeginAsync(ct);
        var row = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.ScanStatus == DocumentRow.Pending, ct);
        if (row is not null)
        {
            await MarkFailedAsync(services, row, services.GetRequiredService<IClock>(),
                $"{attempts} attempts failed, the last with: {error.Message}", ct);
        }

        await unitOfWork.CommitAsync(ct);
        _attempts.TryRemove(id, out _);
        return Outcome.Decided;
    }

    private async Task DeleteQuarantineCopyAsync(IFileStorage storage, string key)
    {
        try
        {
            await storage.DeleteAsync(storageOptions.Buckets.Quarantine, key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The quarantine copy {Key} was not deleted; it expires with the bucket's lifecycle rule.", key);
        }
    }

    private static string Summary(string text) => text.Length <= 500 ? text : text[..497] + "...";
}
