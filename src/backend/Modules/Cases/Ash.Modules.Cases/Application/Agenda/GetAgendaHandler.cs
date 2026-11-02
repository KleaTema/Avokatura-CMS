using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Agenda;

/// <summary>
/// GET /api/agenda (task 031): the hearings of a date range, by role —
/// - the user's own cases (lawyers, assistants, institution jurists, institution users);
/// - case.view_office (Kryetar): the office's cases, optionally of one lawyer;
/// - case.view_all (Monitor/Admin): all, optionally of one office and/or lawyer.
/// A lawyerId or officeId outside the user's scope → 403. Not paged: the range is limited to 62 days and the result
/// to 2,000 hearings.
/// TODO(client-q4): only hearings for now; deadlines, reminders and personal events may join later.
/// TODO(client-q-ics): calendar subscription (Outlook/phone) not built — needs a decision on tokens/security.
/// </summary>
internal sealed class GetAgendaHandler(
    CasesDbContext db,
    AgendaQueryBuilder builder,
    IReferenceData reference,
    IUserDirectory users,
    IOfficeDirectory offices,
    ICurrentUser user,
    IClock clock)
{
    public const int MaxDays = 62;
    public const int MaxItems = 2000;
    private const string InvalidMessage = "Kërkimi nuk është i vlefshëm.";

    public async Task<Result<IReadOnlyList<AgendaItem>>> Handle(AgendaQuery query, CancellationToken ct)
    {
        var errors = new FieldErrors();
        if (query.From is null)
        {
            errors.Add("from", "Zgjidhni datën e fillimit.");
        }

        if (query.To is null)
        {
            errors.Add("to", "Zgjidhni datën e fundit.");
        }

        if (query.From is { } f && query.To is { } t)
        {
            if (t < f)
            {
                errors.Add("to", "Data e fundit nuk mund të jetë para datës së fillimit.");
            }
            else if (t.DayNumber - f.DayNumber + 1 > MaxDays)
            {
                errors.Add("to", $"Periudha mund të jetë deri në {MaxDays} ditë.");
            }
        }

        if (errors.Any)
        {
            return errors.ToError(InvalidMessage);
        }

        var scope = user.HasPermission(CaseScopeQuery.ViewAllPermission) ? AgendaScope.All
            : user.HasPermission(CaseScopeQuery.ViewOfficePermission) && user.OfficeId is not null ? AgendaScope.Office
            : AgendaScope.Own;

        // the office: the Kryetar's own (another one → 403); any for view_all; none for the others (→ 403)
        ushort? officeId = scope == AgendaScope.Office ? (ushort)user.OfficeId!.Value : null;
        if (query.OfficeId is { } officePublicId)
        {
            var id = await offices.GetIdAsync(officePublicId, ct);
            if (id is null)
            {
                return Field("officeId", "Zyra nuk u gjet.");
            }

            if (scope == AgendaScope.Own || (scope == AgendaScope.Office && id != user.OfficeId))
            {
                return Forbidden();
            }

            officeId = (ushort)id.Value;
        }

        uint? lawyerUserId = null;
        if (query.LawyerId is { } lawyerPublicId)
        {
            var lawyer = await users.GetUser(lawyerPublicId, ct);
            var ids = await users.GetIdsAsync([lawyerPublicId], ct);
            if (lawyer is null || !ids.TryGetValue(lawyerPublicId, out var lawyerId))
            {
                return Field("lawyerId", "Avokati nuk u gjet.");
            }

            var allowed = scope switch
            {
                AgendaScope.All => true,
                AgendaScope.Office => lawyer.OfficePublicId is { } lawyerOffice && await offices.GetIdAsync(lawyerOffice, ct) == user.OfficeId,
                _ => lawyerId == user.UserId
            };
            if (!allowed)
            {
                return Forbidden();
            }

            lawyerUserId = (uint)lawyerId;
        }

        var filter = new AgendaFilter(scope, TiraneTime.StartOfDayUtc(query.From!.Value),
            TiraneTime.StartOfDayUtc(query.To!.Value.AddDays(1)), lawyerUserId, officeId, query.IncludeCancelled == true);
        var rows = await builder.Rows(filter).Take(MaxItems + 1).ToListAsync(ct);
        if (rows.Count > MaxItems)
        {
            return Error.Validation("agenda.too_many", "Zgjidhni një periudhë më të shkurtër.");
        }

        return await ItemsAsync(rows, scope, ct);
    }

    private async Task<List<AgendaItem>> ItemsAsync(List<AgendaRow> rows, AgendaScope scope, CancellationToken ct)
    {
        // the current lawyers of the cases in the result: one query, one call to Identity for the names
        var caseIds = rows.Select(r => r.CaseId).Distinct().ToList();
        var lawyers = await db.CaseAssignments.AsNoTracking()
            .Where(a => caseIds.Contains(a.CaseId) && a.EndedAt == null
                        && (a.CaseRole == CaseAssignmentRow.PrimaryLawyer || a.CaseRole == CaseAssignmentRow.SecondaryLawyer))
            .Select(a => new { a.CaseId, a.UserId, a.CaseRole })
            .ToListAsync(ct);
        var primaryOf = lawyers.Where(a => a.CaseRole == CaseAssignmentRow.PrimaryLawyer).ToDictionary(a => a.CaseId, a => a.UserId);
        var names = await users.GetNamesAsync(primaryOf.Values.Select(id => (long)id).Distinct().ToList(), ct);

        // a lawyer's own view: conflicts of any lawyer (primary or secondary) of the cases;
        // the Kryetar's and the Monitor's views: per primary lawyer
        var countsFor = lawyers
            .Where(a => scope == AgendaScope.Own || a.CaseRole == CaseAssignmentRow.PrimaryLawyer)
            .GroupBy(a => a.CaseId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.UserId).ToList());
        var conflicts = AgendaConflicts.Find(rows
            .Where(r => r.Status != CaseHearing.Cancelled)
            .Select(r => (r.HearingId, r.ScheduledAt, (IEnumerable<uint>)countsFor.GetValueOrDefault(r.CaseId, []))));
        var codeOf = rows.ToDictionary(r => r.HearingId, r => r.CaseCode);

        var types = (await reference.ListHearingTypesAsync(ct)).ToDictionary(x => x.Id);
        var courts = new Dictionary<ushort, string>();
        foreach (var courtId in rows.Select(r => r.CourtId).Distinct())
        {
            courts[courtId] = (await reference.GetCourt(courtId, ct))?.Name ?? string.Empty; // cached lookup
        }

        var now = clock.UtcNow;
        return rows.Select(r =>
        {
            var with = conflicts.TryGetValue(r.HearingId, out var others)
                ? others.Select(o => codeOf[o]).Distinct().Order(StringComparer.Ordinal).ToList()
                : [];
            var type = types.TryGetValue(r.HearingTypeId, out var t) ? new CodeName(t.Code, t.Name) : new CodeName(string.Empty, string.Empty);
            return new AgendaItem(r.HearingPublicId, r.CasePublicId, r.CaseCode, DateTime.SpecifyKind(r.ScheduledAt, DateTimeKind.Utc),
                type, r.OtherTypeText, r.Status, courts[r.CourtId],
                primaryOf.TryGetValue(r.CaseId, out var primary) ? names.GetValueOrDefault(primary) : null,
                r.Status == CaseHearing.Scheduled && r.ScheduledAt < now,
                with.Count > 0, with);
        }).ToList();
    }

    private static Error Forbidden() => Error.Forbidden("agenda.forbidden", "Nuk keni leje për këtë agjendë.");

    private static Error Field(string field, string message)
    {
        var errors = new FieldErrors();
        errors.Add(field, message);
        return errors.ToError(InvalidMessage);
    }
}
