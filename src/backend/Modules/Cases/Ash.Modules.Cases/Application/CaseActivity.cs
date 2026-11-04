using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application;

/// <summary><see cref="ICaseActivity"/>: what other modules may do with a case, through the Cases DbContext and its rules.</summary>
internal sealed class CaseActivity(CasesDbContext db, ICaseAccessService access, IHistoryWriter history, FirstDataEntry firstEntry)
    : ICaseActivity
{
    public async Task<CaseRef?> FindViewableAsync(Guid casePublicId, CancellationToken ct = default)
    {
        var c = await RefAsync(db.Cases.AsNoTracking().Where(x => x.PublicId == casePublicId), ct);
        return c is null || !await access.CanViewAsync(c.Id, ct) ? null : c;
    }

    public Task<CaseRef?> FindAsync(long caseId, CancellationToken ct = default)
    {
        var id = (ulong)caseId;
        return RefAsync(db.Cases.AsNoTracking().Where(x => x.Id == id), ct);
    }

    /// <summary>The one case of <paramref name="cases"/> with its closed state and its dossier's archived state (task 038).</summary>
    private async Task<CaseRef?> RefAsync(IQueryable<Case> cases, CancellationToken ct)
    {
        var row = await (
                from c in cases
                join d in db.Dossiers.AsNoTracking() on c.DossierId equals d.Id
                select new { c.Id, c.DossierId, c.CaseCode, c.LifecycleStatus, Archived = d.ArchivedAt != null })
            .SingleOrDefaultAsync(ct);
        return row is null
            ? null
            : new CaseRef((long)row.Id, (long)row.DossierId, row.CaseCode, row.LifecycleStatus == Case.Closed, row.Archived);
    }

    public async Task<IReadOnlyList<CaseLevel>> ListLevelsAsync(long dossierId, CancellationToken ct = default)
    {
        var id = (ulong)dossierId;
        var levels = await db.Cases.AsNoTracking()
            .Where(x => x.DossierId == id)
            .OrderBy(x => x.LevelSeq)
            .Select(x => new { x.Id, x.LevelSeq, x.CaseCode, x.LifecycleStatus })
            .ToListAsync(ct);
        return levels.Select(x => new CaseLevel((long)x.Id, x.LevelSeq, x.CaseCode, x.LifecycleStatus == Case.Closed)).ToList();
    }

    public async Task<DocumentLink?> FindDocumentLinkAsync(long documentId, CancellationToken ct = default)
    {
        var id = (ulong)documentId;
        if (await db.CaseDecisions.AnyAsync(d => d.DecisionDocumentId == id, ct))
        {
            return DocumentLink.Decision;
        }

        return await db.CaseSecurityMeasures.AnyAsync(m => m.DocumentId == id, ct) ? DocumentLink.SecurityMeasure : null;
    }

    public async Task AddHistoryAsync(long dossierId, long? caseId, HistoryEventType type, string summary, object? @new = null, CancellationToken ct = default)
    {
        history.Add(dossierId, caseId, type, summary, old: null, @new: @new);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFirstDataEntryAsync(long caseId, CancellationToken ct = default)
    {
        var id = (ulong)caseId;
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is not null)
        {
            await firstEntry.MarkAsync(c, ct);
        }
    }
}
