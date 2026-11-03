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
        var c = await db.Cases.AsNoTracking()
            .Where(x => x.PublicId == casePublicId)
            .Select(x => new { x.Id, x.DossierId, x.CaseCode, x.LifecycleStatus })
            .SingleOrDefaultAsync(ct);
        return c is null || !await access.CanViewAsync((long)c.Id, ct)
            ? null
            : new CaseRef((long)c.Id, (long)c.DossierId, c.CaseCode, c.LifecycleStatus == Case.Closed);
    }

    public async Task<CaseRef?> FindAsync(long caseId, CancellationToken ct = default)
    {
        var id = (ulong)caseId;
        var c = await db.Cases.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.DossierId, x.CaseCode, x.LifecycleStatus })
            .SingleOrDefaultAsync(ct);
        return c is null ? null : new CaseRef((long)c.Id, (long)c.DossierId, c.CaseCode, c.LifecycleStatus == Case.Closed);
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
