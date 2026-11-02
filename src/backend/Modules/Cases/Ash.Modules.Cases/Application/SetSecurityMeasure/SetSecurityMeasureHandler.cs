using System.Globalization;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.SetSecurityMeasure;

/// <summary>
/// PUT security-measure (case.edit_data). The case_security_measures row is never deleted: lifting only sets
/// cases.has_security_measure = 0 (SECURITY_MEASURE_LIFTED with the measure as it was); setting it again updates the
/// row, and the replaced values go to the history (SECURITY_MEASURE_SET old/new).
/// TODO(task-033): link the decision document (there is no document_id column yet — do not add one here).
/// </summary>
internal sealed class SetSecurityMeasureHandler(
    CasesDbContext db, CaseDataGate gate, FirstDataEntry firstEntry, IHistoryWriter history, IAuditLog audit,
    ICurrentUser user, IClock clock)
{
    public const string Permission = "case.edit_data";

    public async Task<Result<SetSecurityMeasureResponse>> Handle(Guid casePublicId, SetSecurityMeasureRequest req, CancellationToken ct)
    {
        var invalid = SetSecurityMeasureValidator.Validate(req);
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
        var row = await db.CaseSecurityMeasures.SingleOrDefaultAsync(m => m.CaseId == c.Id, ct);
        var before = row is null ? null : Snapshot(row);
        var dossierId = (long)c.DossierId;
        var caseId = (long)c.Id;

        if (req.Active!.Value)
        {
            var number = req.DecisionNumber!.Trim();
            var date = req.DecisionDate!.Value;
            var dispositive = string.IsNullOrWhiteSpace(req.Dispositive) ? null : req.Dispositive.Trim();
            if (c.HasSecurityMeasure && row is not null
                && row.DecisionNumber == number && row.DecisionDate == date && row.Dispositive == dispositive)
            {
                return CaseDataGate.NoChange();
            }

            if (row is null)
            {
                row = new CaseSecurityMeasure { CaseId = c.Id, RecordedAt = clock.UtcNow };
                db.CaseSecurityMeasures.Add(row);
            }

            row.DecisionNumber = number;
            row.DecisionDate = date;
            row.Dispositive = dispositive;
            row.RecordedBy = (uint)user.UserId;
            c.HasSecurityMeasure = true;
            history.Add(dossierId, caseId, HistoryEventType.SecurityMeasureSet,
                $"Masa e sigurimit u vendos: vendimi nr. {number}, datë {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}.",
                old: before, @new: Snapshot(row));
        }
        else
        {
            if (!c.HasSecurityMeasure)
            {
                return CaseDataGate.NoChange();
            }

            c.HasSecurityMeasure = false;
            history.Add(dossierId, caseId, HistoryEventType.SecurityMeasureLifted,
                row is null ? "Masa e sigurimit u hoq." : $"Masa e sigurimit u hoq (vendimi nr. {row.DecisionNumber}).",
                old: before);
        }

        if (await gate.SaveAsync(c, ct) is { } conflict)
        {
            return conflict;
        }

        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", caseId, caseId,
            details: new { Action = c.HasSecurityMeasure ? "SET_SECURITY_MEASURE" : "LIFT_SECURITY_MEASURE" });
        return new SetSecurityMeasureResponse(c.RowVersion, c.HasSecurityMeasure,
            c.HasSecurityMeasure && row is not null ? new SecurityMeasureView(row.DecisionNumber, row.DecisionDate, row.Dispositive) : null);
    }

    // the date as yyyy-MM-dd text (a calendar date, no time zone)
    private static object Snapshot(CaseSecurityMeasure m) =>
        new { m.DecisionNumber, DecisionDate = m.DecisionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), m.Dispositive };
}
