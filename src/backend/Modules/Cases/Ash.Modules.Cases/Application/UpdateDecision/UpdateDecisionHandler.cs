using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.RecordDecision;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.UpdateDecision;

/// <summary>
/// PUT /api/cases/{id}/decision (task 037): completes the decision of a CLOSED level — the reasoned decision arrives
/// (received + its VENDIM document, decision_received_at set), the appeal is filed, it becomes final. The case stays
/// CLOSED and its row is not touched; the decision's own row_version is checked and incremented. Only changed fields
/// are written; DECISION_UPDATED keeps old/new of those only.
/// Rules: received cannot be undone (true → false → 400). When a NEWER level of the dossier exists the decision was
/// appealed: becoming final, or clearing "Ankimuar"/"Rekursuar", is refused (409 "Kjo çështje ka vazhduar në …").
/// An archived dossier refuses the change (task 038: 409 "Dosja është e arkivuar.").
/// </summary>
internal sealed class UpdateDecisionHandler(
    CasesDbContext db,
    ICaseAccessService access,
    DecisionSupport support,
    IHistoryWriter history,
    IAuditLog audit,
    ICurrentUser user,
    IClock clock)
{
    public async Task<Result<UpdateDecisionResponse>> Handle(Guid casePublicId, DecisionRequest req, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        if (await access.IsDossierArchivedAsync((long)c.Id, ct))
        {
            return DossierErrors.Archived;
        }

        var decision = await db.CaseDecisions.SingleOrDefaultAsync(d => d.CaseId == c.Id, ct);
        if (c.LifecycleStatus != Case.Closed || decision is null)
        {
            return Error.Conflict("decision.not_recorded", "Çështja nuk është mbyllur ende; regjistroni vendimin për ta mbyllur.");
        }

        if (!(await access.GetEffectivePermissionsAsync((long)c.Id, ct)).Contains(DecisionSupport.Permission))
        {
            return AssignLawyersHandler.Forbidden();
        }

        var parsed = await support.ValidateAsync(req, c, ct);
        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        var values = parsed.Value;
        if (decision.DecisionReceived && !values.DecisionReceived)
        {
            var errors = new FieldErrors();
            errors.Add("decisionReceived", "Vendimi i arsyetuar është shënuar si i ardhur; kjo nuk zhbëhet.");
            return errors.ToError("Të dhënat e vendimit nuk janë të vlefshme.");
        }

        ulong? documentId = decision.DecisionDocumentId;
        if (values.DecisionDocumentPublicId is { } documentPublicId)
        {
            var document = await support.DocumentAsync(documentPublicId, c, ct);
            if (document.IsFailure)
            {
                return document.Error!;
            }

            documentId = (ulong)document.Value;
        }

        if (decision.RowVersion != req.RowVersion)
        {
            return AssignLawyersHandler.Changed();
        }

        // the dossier went on to a newer level: the decision was appealed and is not final
        var becomesFinal = values.IsFinal && !decision.IsFinal;
        var clearsAppeal = (decision.IsAppealed && !values.IsAppealed) || (decision.IsRecursed && !values.IsRecursed);
        if (becomesFinal || clearsAppeal)
        {
            var newer = await db.Cases.AsNoTracking()
                .Where(x => x.DossierId == c.DossierId && x.LevelSeq > c.LevelSeq)
                .OrderBy(x => x.LevelSeq)
                .Select(x => x.CaseCode)
                .FirstOrDefaultAsync(ct);
            if (newer is not null)
            {
                return Error.Conflict("decision.continued", $"Kjo çështje ka vazhduar në {newer}");
            }
        }

        var before = await support.SnapshotAsync(decision, ct);
        var wasFinal = decision.IsFinal;
        DecisionSupport.Apply(decision, values);
        decision.DecisionDocumentId = documentId;
        if (values.DecisionReceived && !decision.DecisionReceived)
        {
            decision.DecisionReceived = true;
            decision.DecisionReceivedAt = clock.UtcNow;
        }

        var after = await support.SnapshotAsync(decision, ct);
        var changed = after.Keys.Where(k => !Equals(before[k], after[k])).ToList();
        if (changed.Count == 0)
        {
            return CaseDataGate.NoChange();
        }

        decision.UpdatedBy = (uint)user.UserId;
        decision.RowVersion++;
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.DecisionUpdated, "Vendimi u plotësua.",
            old: changed.ToDictionary(k => k, k => before[k]),
            @new: changed.ToDictionary(k => k, k => after[k]));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AssignLawyersHandler.Changed();
        }

        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id,
            details: new { Action = "UPDATE_DECISION", Fields = changed });
        // the result changed to a retrial, or the decision became final (the appeal deadline passed): offer the next step
        var suggestions = DecisionSupport.Suggestions(values.Result, decision.IsFinal && !wasFinal)
            .Where(s => s != DecisionSuggestions.RetrialExpected || changed.Contains("result"))
            .ToList();
        return new UpdateDecisionResponse(await support.ViewAsync(decision, ct), suggestions);
    }
}
