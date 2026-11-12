using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Documents.Contracts;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.ArchiveDossier;

/// <summary>
/// When a dossier may be archived (task 038, tightened by the client's decision of 12.11.2026, section A): ONLY from its
/// LATEST level, which is CLOSED with a decision that is FINAL (formë e prerë), whose reasoned decision has ARRIVED and
/// whose decision document is linked, not removed and CLEAN (scanned). The one place of the rule: the archive
/// endpoint answers with its reason (409), CaseAccessService grants decision.close_final only when it holds (so the
/// "Arkivo" button and the "Arkivo çështjen?" offer follow it), and record / update decision suggest archiving when a
/// level becomes archivable — also when the document is added later (task 037).
/// </summary>
public static class ArchiveRule
{
    public const string Clean = "CLEAN";

    /// <summary>Null when the level can close the dossier; otherwise the reason, in the order the user can fix it.</summary>
    public static async Task<Error?> CheckAsync(
        CasesDbContext db, IDocumentService documents, Case level, CaseDecision? decision, CancellationToken ct)
    {
        if (await db.Cases.AnyAsync(n => n.DossierId == level.DossierId && n.LevelSeq > level.LevelSeq, ct))
        {
            return Error.Conflict("dossier.not_latest_level",
                $"{level.CaseCode} nuk është shkalla e fundit e dosjes; dosja arkivohet nga shkalla e fundit.");
        }

        if (level.LifecycleStatus != Case.Closed)
        {
            return Error.Conflict("dossier.latest_not_closed",
                $"Shkalla e fundit e dosjes ({level.CaseCode}) nuk është mbyllur ende; dosja arkivohet pas vendimit të formës së prerë.");
        }

        if (decision is null || !decision.IsFinal)
        {
            return Error.Conflict("dossier.not_final",
                $"Vendimi i shkallës {level.CaseCode} nuk ka marrë formë të prerë; shënojeni më parë si të formës së prerë.");
        }

        if (!decision.DecisionReceived)
        {
            return Error.Conflict("dossier.reasoned_missing",
                $"Vendimi i arsyetuar i shkallës {level.CaseCode} nuk ka ardhur ende; dosja arkivohet pasi të regjistrohet.");
        }

        var document = decision.DecisionDocumentId is { } id ? await documents.GetByIdAsync((long)id, ct) : null;
        if (document is null || document.IsDeleted)
        {
            return Error.Conflict("dossier.document_missing",
                $"Dokumenti i vendimit të shkallës {level.CaseCode} nuk është ngarkuar; ngarkojeni dhe lidheni me vendimin para arkivimit.");
        }

        if (document.ScanStatus != Clean)
        {
            return Error.Conflict("dossier.document_not_clean",
                $"Dokumenti i vendimit të shkallës {level.CaseCode} nuk ka kaluar kontrollin e sigurisë; arkivimi pritet derisa të jetë i pastër.");
        }

        return null;
    }

    /// <summary>The same rule for a level by id (its decision loaded here).</summary>
    public static async Task<bool> IsArchivableAsync(CasesDbContext db, IDocumentService documents, Case level, CancellationToken ct)
    {
        var decision = await db.CaseDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.CaseId == level.Id, ct);
        return await CheckAsync(db, documents, level, decision, ct) is null;
    }
}
