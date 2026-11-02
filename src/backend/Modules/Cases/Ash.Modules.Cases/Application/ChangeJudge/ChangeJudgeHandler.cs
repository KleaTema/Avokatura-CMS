using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.ChangeJudge;

/// <summary>
/// PATCH judge (case.change_judge: the primary, a delegated institution, the registering Protokoll while REGISTERED).
/// The judge must exist and be active; a judge listed at another court is allowed with a warning (the KLGJ list may
/// lag), as at registration. JUDGE_CHANGED keeps old and new {name, courtName}.
/// </summary>
internal sealed class ChangeJudgeHandler(
    CaseDataGate gate, FirstDataEntry firstEntry, IReferenceData reference, IHistoryWriter history, IAuditLog audit)
{
    public const string Permission = "case.change_judge";

    public async Task<Result<ChangeJudgeResponse>> Handle(Guid casePublicId, ChangeJudgeRequest req, CancellationToken ct)
    {
        var invalid = ChangeJudgeValidator.Validate(req);
        if (invalid is not null)
        {
            return invalid;
        }

        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, [Permission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var judge = await reference.GetJudge((uint)req.JudgeId!.Value, ct);
        if (judge is null || !judge.IsActive)
        {
            return Error.ValidationFields(ChangeJudgeValidator.InvalidMessage, new Dictionary<string, string[]>
            {
                ["judgeId"] = [$"Gjyqtari nuk u gjet ose nuk është aktiv. {RegisterCaseValidator.JudgeMissingHint}"]
            });
        }

        if (c.JudgeId == judge.Id)
        {
            return CaseDataGate.NoChange();
        }

        var warnings = new List<RegistrationWarning>();
        if (judge.CourtId != c.CourtId)
        {
            // the KLGJ list may be outdated: a warning, not an error
            warnings.Add(new RegistrationWarning("judge.court_mismatch",
                $"Gjyqtari {judge.FirstName} {judge.LastName} figuron në listë në një gjykatë tjetër. Kontrolloni të dhënat."));
        }

        var old = c.JudgeId is { } oldId ? await reference.GetJudge(oldId, ct) : null;
        var oldName = old is null ? null : $"{old.FirstName} {old.LastName}";
        var newName = $"{judge.FirstName} {judge.LastName}";
        c.JudgeId = judge.Id;
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.JudgeChanged,
            oldName is null ? $"Gjyqtari u caktua: {newName}." : $"Gjyqtari u ndryshua: {oldName} → {newName}.",
            old: new { Name = oldName, CourtName = old is null ? null : await CourtNameAsync(old.CourtId, ct) },
            @new: new { Name = newName, CourtName = await CourtNameAsync(judge.CourtId, ct) });
        if (await gate.SaveAsync(c, ct) is { } conflict)
        {
            return conflict;
        }

        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id, details: new { Action = "CHANGE_JUDGE", JudgeId = judge.Id });
        return new ChangeJudgeResponse(c.RowVersion, new JudgeView(judge.Id, newName), warnings);
    }

    private async Task<string?> CourtNameAsync(ushort? courtId, CancellationToken ct) =>
        courtId is { } id ? (await reference.GetCourt(id, ct))?.Name : null;
}
