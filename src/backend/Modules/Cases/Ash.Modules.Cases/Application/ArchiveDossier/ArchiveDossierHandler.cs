using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.Modules.Documents.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.ArchiveDossier;

/// <summary>rowVersion: the dossier's LATEST case, as GET /api/cases/{id} returned it.</summary>
public sealed record ArchiveDossierRequest(uint? RowVersion);

/// <summary>The latest case's new rowVersion (archiving touches it, so a change made meanwhile is a 409).</summary>
public sealed record ArchiveDossierResponse(DateTime ArchivedAt, uint CaseRowVersion);

/// <summary>
/// POST /api/dossiers/{id}/archive (task 038). Checks, in order: the dossier or its latest level not viewable → 404;
/// already archived → 409 "Dosja është e arkivuar."; the latest level not archivable (ArchiveRule: not CLOSED, not final,
/// reasoned decision not arrived, no clean decision document) → 409 with the reason; no decision.close_final on the latest level (effective permissions) → 403; stale rowVersion → 409.
/// Sets archived_at / archived_by; the latest case's row_version + 1; history DOSSIER_ARCHIVED on the latest case;
/// audit; DossierArchived published. From then on every level is read-only (CaseAccessService).
/// TODO(client-q-unarchive): undoing an archiving — not possible now; later admin-only with a reason and audit.
/// </summary>
internal sealed class ArchiveDossierHandler(
    CasesDbContext db,
    ICaseAccessService access,
    IHistoryWriter history,
    IAuditLog audit,
    IEventPublisher events,
    IDocumentService documents,
    ICurrentUser user,
    IClock clock)
{
    public async Task<Result<ArchiveDossierResponse>> Handle(Guid dossierPublicId, ArchiveDossierRequest req, CancellationToken ct)
    {
        if (req.RowVersion is null)
        {
            var errors = new FieldErrors();
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
            return errors.ToError("Të dhënat nuk janë të vlefshme.");
        }

        var dossier = await db.Dossiers.SingleOrDefaultAsync(d => d.PublicId == dossierPublicId, ct);
        var latest = dossier is null
            ? null
            : await db.Cases.Where(c => c.DossierId == dossier.Id).OrderByDescending(c => c.LevelSeq).FirstOrDefaultAsync(ct);
        if (dossier is null || latest is null || !await access.CanViewAsync((long)latest.Id, ct))
        {
            return Error.NotFound("dossier.not_found", "Dosja nuk u gjet.");
        }

        if (dossier.ArchivedAt is not null)
        {
            return DossierErrors.Archived;
        }

        // closed, final, reasoned decision arrived, its document linked and clean (client decision 12.11.2026, A)
        var decision = await db.CaseDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.CaseId == latest.Id, ct);
        if (await ArchiveRule.CheckAsync(db, documents, latest, decision, ct) is { } notYet)
        {
            return notYet;
        }

        if (!(await access.GetEffectivePermissionsAsync((long)latest.Id, ct)).Contains(CaseAccessService.CloseFinalPermission))
        {
            return AssignLawyersHandler.Forbidden();
        }

        if (latest.RowVersion != req.RowVersion)
        {
            return AssignLawyersHandler.Changed();
        }

        var now = clock.UtcNow;
        dossier.ArchivedAt = now;
        dossier.ArchivedBy = (uint)user.UserId;
        latest.RowVersion++;
        history.Add((long)dossier.Id, (long)latest.Id, HistoryEventType.DossierArchived,
            $"Dosja {dossier.UniqueCode} u arkivua (vendim i formës së prerë në {latest.CaseCode}).",
            @new: new { ArchivedAt = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AssignLawyersHandler.Changed();
        }

        audit.Record(AuditActions.CaseUpdate, "DOSSIER", (long)dossier.Id, (long)latest.Id,
            details: new { Action = "ARCHIVE_DOSSIER", dossier.UniqueCode });
        var levels = await db.Cases.AsNoTracking().Where(c => c.DossierId == dossier.Id).OrderBy(c => c.LevelSeq)
            .Select(c => c.PublicId).ToListAsync(ct);
        // TODO(chat): the Chat module listens and makes the case chats read-only
        await events.PublishAsync(new DossierArchived(dossier.PublicId, dossier.UniqueCode, levels, now), ct);
        return new ArchiveDossierResponse(now, latest.RowVersion);
    }
}
