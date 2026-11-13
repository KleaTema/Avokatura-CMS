using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using SupervisionRow = Ash.Modules.Cases.Infrastructure.Supervision;

namespace Ash.Modules.Cases.Application.Supervision;

public sealed record SupervisionPerson(Guid PublicId, string Name, string? OfficeName);

public sealed record SupervisionCase(Guid PublicId, string CaseCode);

/// <summary>One supervision, as the Monitor's "Mbikëqyrja" screen lists it.</summary>
public sealed record SupervisionItem(
    Guid PublicId,
    SupervisionPerson Head,
    string Kind,
    SupervisionPerson? Lawyer,
    SupervisionCase? Case,
    int CoveredCases,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime? EndedAt);

/// <summary>Who the Monitor may pick: the active Kryetar (OFFICE_HEAD) and the active lawyers (STATE_LAWYER).</summary>
public sealed record SupervisionPeople(IReadOnlyList<SupervisionPerson> Heads, IReadOnlyList<SupervisionPerson> Lawyers);

/// <summary>kind LAWYER → lawyerUserPublicId; kind CASE → caseCode (as the Monitor reads it on the case).</summary>
public sealed record CreateSupervisionRequest(Guid? HeadUserPublicId, string? Kind, Guid? LawyerUserPublicId, string? CaseCode);

/// <summary>
/// "Mbikëqyrja" (task 053d, client decision C, default D3 — both kinds). The Monitor (supervision.manage) puts a
/// LAWYER (every case where they are the CURRENT primary — decided at read time, so later cases join and transferred
/// ones leave) or one CASE under a Kryetar's supervision, and ends it. The Kryetar gets a mandatory notification;
/// a CASE supervision writes SUPERVISION_STARTED / _ENDED on the case. Rules: the Kryetar must be an active
/// OFFICE_HEAD; a lawyer an active STATE_LAWYER of ANOTHER office (their own office is theirs anyway); a case of
/// another office; one active row per (Kryetar, lawyer) and (Kryetar, case) → 409.
/// </summary>
internal sealed class SupervisionHandlers(
    CasesDbContext db,
    IUserDirectory users,
    IOfficeDirectory offices,
    IHistoryWriter history,
    IAuditLog audit,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public const string HeadRole = "OFFICE_HEAD";
    public const string LawyerRole = "STATE_LAWYER";
    public const int MaxListed = 500;

    public async Task<SupervisionPeople> PeopleAsync(CancellationToken ct)
    {
        var officeNames = (await offices.ListActiveAsync(ct)).ToDictionary(o => o.Id, o => o.Name);
        SupervisionPerson Person(RoleUser u) =>
            new(u.PublicId, u.FullName, u.OfficeId is { } o ? officeNames.GetValueOrDefault(o) : null);
        var heads = (await users.GetUsersByRoleAsync(HeadRole, ct)).OrderBy(u => u.FullName).Select(Person).ToList();
        var lawyers = (await users.GetUsersByRoleAsync(LawyerRole, ct)).OrderBy(u => u.FullName).Select(Person).ToList();
        return new SupervisionPeople(heads, lawyers);
    }

    /// <summary>The supervisions, active first then the newest ended (at most <see cref="MaxListed"/>).</summary>
    public async Task<IReadOnlyList<SupervisionItem>> ListAsync(bool includeEnded, CancellationToken ct)
    {
        var rows = await db.Supervisions.AsNoTracking()
            .Where(s => includeEnded || s.EndedAt == null)
            .OrderBy(s => s.EndedAt != null).ThenByDescending(s => s.EndedAt).ThenByDescending(s => s.CreatedAt)
            .Take(MaxListed)
            .ToListAsync(ct);
        return await ItemsAsync(rows, ct);
    }

    public async Task<Result<SupervisionItem>> CreateAsync(CreateSupervisionRequest req, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var heads = await users.GetUsersByRoleAsync(HeadRole, ct);
        var head = req.HeadUserPublicId is { } headId ? heads.FirstOrDefault(h => h.PublicId == headId) : null;
        if (head is null)
        {
            errors.Add("headUserPublicId", "Zgjidhni një Kryetar aktiv.");
        }

        var kind = req.Kind?.Trim().ToUpperInvariant();
        RoleUser? lawyer = null;
        Case? target = null;
        if (kind == SupervisionRow.LawyerKind)
        {
            var lawyers = await users.GetUsersByRoleAsync(LawyerRole, ct);
            lawyer = req.LawyerUserPublicId is { } lawyerId ? lawyers.FirstOrDefault(l => l.PublicId == lawyerId) : null;
            if (lawyer is null)
            {
                errors.Add("lawyerUserPublicId", "Zgjidhni një avokat aktiv.");
            }
            else if (head is not null && (lawyer.Id == head.Id || lawyer.OfficeId == head.OfficeId))
            {
                errors.Add("lawyerUserPublicId", "Avokati është i zyrës së këtij Kryetari: çështjet e tij i sheh tashmë.");
            }
        }
        else if (kind == SupervisionRow.CaseKind)
        {
            var code = req.CaseCode?.Trim();
            target = string.IsNullOrEmpty(code) ? null : await db.Cases.AsNoTracking().SingleOrDefaultAsync(c => c.CaseCode == code, ct);
            if (target is null)
            {
                errors.Add("caseCode", "Çështja me këtë kod nuk u gjet.");
            }
            else if (head is not null && target.OfficeId == head.OfficeId)
            {
                errors.Add("caseCode", "Çështja është e zyrës së këtij Kryetari: e sheh tashmë.");
            }
        }
        else
        {
            errors.Add("kind", "Zgjidhni: avokat ose çështje.");
        }

        if (errors.Any)
        {
            return errors.ToError("Mbikëqyrja nuk është e vlefshme.");
        }

        var headUser = (uint)head!.Id;
        var exists = await db.Supervisions.AnyAsync(s => s.HeadUserId == headUser && s.EndedAt == null
            && ((lawyer != null && s.Kind == SupervisionRow.LawyerKind && s.LawyerUserId == (uint)lawyer.Id)
                || (target != null && s.Kind == SupervisionRow.CaseKind && s.CaseId == target.Id)), ct);
        if (exists)
        {
            return AlreadyActive();
        }

        var row = new SupervisionRow
        {
            PublicId = Guid.NewGuid(),
            HeadUserId = headUser,
            Kind = kind!,
            LawyerUserId = lawyer is null ? null : (uint)lawyer.Id,
            CaseId = target?.Id,
            CreatedBy = (uint)user.UserId,
            CreatedAt = clock.UtcNow,
        };
        db.Supervisions.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("uq_sv_active", StringComparison.Ordinal) == true)
        {
            return AlreadyActive(); // a parallel request was first
        }

        if (target is not null)
        {
            history.Add((long)target.DossierId, (long)target.Id, HistoryEventType.SupervisionStarted,
                $"Çështja u vendos në mbikëqyrje të Kryetarit {head.FullName}.", @new: new { Head = head.FullName });
            await db.SaveChangesAsync(ct);
        }

        audit.Record(AuditActions.AdminChange, "SUPERVISION", (long)row.Id, target is null ? null : (long)target.Id,
            details: new { Action = "SUPERVISION_STARTED", Head = head.FullName, Kind = row.Kind, Lawyer = lawyer?.FullName, Case = target?.CaseCode });
        await events.PublishAsync(new SupervisionStarted(head.PublicId, lawyer?.FullName, target?.PublicId), ct);
        return (await ItemsAsync([row], ct))[0];
    }

    public async Task<Result<SupervisionItem>> EndAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Supervisions.SingleOrDefaultAsync(s => s.PublicId == publicId, ct);
        if (row is null)
        {
            return Error.NotFound("supervision.not_found", "Mbikëqyrja nuk u gjet.");
        }

        if (row.EndedAt is not null)
        {
            return Error.Conflict("supervision.ended", "Kjo mbikëqyrje ka përfunduar tashmë.");
        }

        row.EndedAt = clock.UtcNow;
        row.EndedBy = (uint)user.UserId;
        await db.SaveChangesAsync(ct);

        var refs = await users.GetRefsAsync([row.HeadUserId, .. row.LawyerUserId is { } l ? new long[] { l } : []], ct);
        var head = refs[row.HeadUserId];
        var lawyerName = row.LawyerUserId is { } lawyerId ? refs.GetValueOrDefault(lawyerId)?.Name : null;
        Case? target = null;
        if (row.CaseId is { } caseId)
        {
            target = await db.Cases.AsNoTracking().SingleAsync(c => c.Id == caseId, ct);
            history.Add((long)target.DossierId, (long)target.Id, HistoryEventType.SupervisionEnded,
                $"Mbikëqyrja e Kryetarit {head.Name} përfundoi.", old: new { Head = head.Name });
            await db.SaveChangesAsync(ct);
        }

        audit.Record(AuditActions.AdminChange, "SUPERVISION", (long)row.Id, row.CaseId is { } id ? (long)id : null,
            details: new { Action = "SUPERVISION_ENDED", Head = head.Name, row.Kind, Lawyer = lawyerName, Case = target?.CaseCode });
        await events.PublishAsync(new SupervisionEnded(head.PublicId, lawyerName, target?.PublicId), ct);
        return (await ItemsAsync([row], ct))[0];
    }

    private static Error AlreadyActive() =>
        Error.Conflict("supervision.exists", "Ky Kryetar e ka tashmë këtë mbikëqyrje.");

    private async Task<List<SupervisionItem>> ItemsAsync(List<SupervisionRow> rows, CancellationToken ct)
    {
        var userIds = rows.SelectMany(r => new long?[] { r.HeadUserId, r.LawyerUserId, r.CreatedBy })
            .OfType<long>().Distinct().ToList();
        var refs = await users.GetRefsAsync(userIds, ct);
        var states = (await users.GetStatesAsync(userIds, [], ct)).ToDictionary(s => s.Id);
        var officeNames = (await offices.ListActiveAsync(ct)).ToDictionary(o => o.PublicId, o => o.Name);
        var summaries = new Dictionary<long, SupervisionPerson>();
        foreach (var id in userIds)
        {
            var summary = refs.TryGetValue(id, out var r) ? await users.GetUser(r.PublicId, ct) : null;
            var office = summary?.OfficePublicId is { } o ? officeNames.GetValueOrDefault(o) : null;
            summaries[id] = new SupervisionPerson(r?.PublicId ?? Guid.Empty, r?.Name ?? states.GetValueOrDefault(id)?.Name ?? string.Empty, office);
        }

        var caseIds = rows.Where(r => r.CaseId != null).Select(r => r.CaseId!.Value).ToList();
        var cases = await db.Cases.AsNoTracking().Where(c => caseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new SupervisionCase(c.PublicId, c.CaseCode), ct);
        // a lawyer's covered cases: those where they are the current primary (not closed)
        var lawyerIds = rows.Where(r => r.LawyerUserId != null).Select(r => r.LawyerUserId!.Value).Distinct().ToList();
        var covered = await (from a in db.CaseAssignments.AsNoTracking()
                             join c in db.Cases.AsNoTracking() on a.CaseId equals c.Id
                             where lawyerIds.Contains(a.UserId) && a.EndedAt == null && a.CaseRole == CaseAssignmentRow.PrimaryLawyer
                                   && c.LifecycleStatus != Case.Closed
                             group a by a.UserId into g
                             select new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return rows.Select(r => new SupervisionItem(
                r.PublicId,
                summaries[r.HeadUserId],
                r.Kind,
                r.LawyerUserId is { } l ? summaries[l] : null,
                r.CaseId is { } c ? cases.GetValueOrDefault(c) : null,
                r.LawyerUserId is { } lawyer ? covered.GetValueOrDefault(lawyer) : 1,
                r.CreatedAt,
                summaries[r.CreatedBy].Name,
                r.EndedAt))
            .ToList();
    }
}
