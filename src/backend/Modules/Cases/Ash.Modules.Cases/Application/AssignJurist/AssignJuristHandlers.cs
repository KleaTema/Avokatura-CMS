using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.AssignJurist;

/// <summary>
/// The institution's jurist on a case (task 027). An INSTITUTION_USER with institution.assign_jurist acts for its OWN
/// institution, and only on cases that institution is linked to (GetEffectivePermissionsAsync grants the permission
/// only there). One current jurist per institution per case; the jurist's profile follows the link
/// (INSTITUTION_MONITORING or INSTITUTION_DELEGATED, v_case_access).
/// </summary>
internal sealed class InstitutionJurists(CasesDbContext db, IUserDirectory users)
{
    public const string JuristRole = "INSTITUTION_JURIST";

    /// <summary>The active INSTITUTION_JURISTs of the institution.</summary>
    public Task<IReadOnlyList<OfficeUser>> CandidatesAsync(long institutionId, CancellationToken ct) =>
        users.GetInstitutionUsersByRoleAsync(institutionId, JuristRole, ct);

    /// <summary>The current INSTITUTION_JURIST rows of the case that belong to the institution (normally one), tracked.</summary>
    public async Task<List<(CaseAssignmentRow Row, UserRef Jurist)>> CurrentAsync(ulong caseId, long institutionId, CancellationToken ct)
    {
        var rows = await db.CaseAssignments
            .Where(a => a.CaseId == caseId && a.CaseRole == CaseAssignmentRow.InstitutionJurist && a.EndedAt == null)
            .ToListAsync(ct);
        var refs = await users.GetRefsAsync(rows.Select(r => (long)r.UserId).ToList(), ct);
        return rows.Where(r => refs.TryGetValue(r.UserId, out var u) && u.InstitutionId == institutionId)
            .Select(r => (r, refs[r.UserId]))
            .ToList();
    }
}

/// <summary>GET assignable-jurists: the jurists of the user's institution, the current one marked, by name.</summary>
internal sealed class AssignableJuristsHandler(
    CasesDbContext db, ICaseAccessService access, InstitutionJurists jurists, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<AssignableJurist>>> Handle(Guid casePublicId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking()
            .Where(x => x.PublicId == casePublicId)
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        if (user.InstitutionId is not { } institutionId
            || !(await access.GetEffectivePermissionsAsync((long)c.Id, ct)).Contains(CaseAccessService.AssignJuristPermission))
        {
            return AssignLawyersHandler.Forbidden();
        }

        var current = (await jurists.CurrentAsync(c.Id, institutionId, ct)).Select(x => x.Jurist.PublicId).ToHashSet();
        return (await jurists.CandidatesAsync(institutionId, ct))
            .Select(u => new AssignableJurist(u.PublicId, u.FullName, current.Contains(u.PublicId)))
            .OrderBy(x => x.FullName, OfficeLawyers.NameOrder)
            .ToList();
    }
}

/// <summary>
/// POST jurist: assigns a jurist of the user's institution; a current one is replaced — its row ENDED (REMOVED) first,
/// then the new row inserted (unique slots). JURIST_ASSIGNED (old/new {name, userPublicId}), JuristAssigned.
/// </summary>
internal sealed class AssignJuristHandler(
    CasesDbContext db, CaseDataGate gate, InstitutionJurists jurists, IInstitutionDirectory institutions,
    IHistoryWriter history, IAuditLog audit, IEventPublisher events, ICurrentUser user, IClock clock)
{
    public async Task<Result<AssignJuristResponse>> Handle(Guid casePublicId, AssignJuristRequest req, CancellationToken ct)
    {
        var errors = new FieldErrors();
        if (req.UserPublicId is null || req.UserPublicId == Guid.Empty)
        {
            errors.Add("userPublicId", "Zgjidhni juristin.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        if (errors.Any)
        {
            return errors.ToError("Të dhënat e juristit nuk janë të vlefshme.");
        }

        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, [CaseAccessService.AssignJuristPermission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var institutionId = user.InstitutionId!.Value; // the permission is only granted with a linked institution
        var jurist = (await jurists.CandidatesAsync(institutionId, ct)).FirstOrDefault(u => u.PublicId == req.UserPublicId);
        if (jurist is null)
        {
            return Error.ValidationFields("Të dhënat e juristit nuk janë të vlefshme.", new Dictionary<string, string[]>
            {
                ["userPublicId"] = ["Ky përdorues nuk është jurist aktiv i institucionit tuaj."]
            });
        }

        var current = await jurists.CurrentAsync(c.Id, institutionId, ct);
        if (current.Count == 1 && current[0].Jurist.PublicId == jurist.PublicId)
        {
            return CaseDataGate.NoChange();
        }

        var now = clock.UtcNow;
        var me = (uint)user.UserId;
        try
        {
            // 1. end the current jurist's row(s), 2. insert the new one
            foreach (var (row, _) in current)
            {
                row.EndedAt = now;
                row.EndedBy = me;
                row.EndReason = CaseAssignmentRow.Removed;
            }

            await db.SaveChangesAsync(ct);
            db.CaseAssignments.Add(new CaseAssignmentRow
            {
                CaseId = c.Id,
                UserId = (uint)jurist.Id,
                CaseRole = CaseAssignmentRow.InstitutionJurist,
                AssignedBy = me,
                AssignedAt = now
            });
            var previous = current.Count > 0 ? current[0].Jurist : null;
            history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.JuristAssigned,
                previous is null
                    ? $"Juristi i institucionit u caktua: {jurist.FullName}."
                    : $"Juristi i institucionit u ndryshua: {previous.Name} → {jurist.FullName}.",
                old: previous is null ? null : new { previous.Name, UserPublicId = previous.PublicId },
                @new: new { Name = jurist.FullName, UserPublicId = jurist.PublicId });
            if (await gate.SaveAsync(c, ct) is { } conflict)
            {
                return conflict;
            }

            var institution = (await institutions.GetByIdsAsync([institutionId], ct)).Single();
            audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id,
                details: new { Action = "ASSIGN_JURIST", UserPublicId = jurist.PublicId });
            await events.PublishAsync(new JuristAssigned(c.PublicId, institution.PublicId, jurist.PublicId, previous?.PublicId), ct);
            return new AssignJuristResponse(c.RowVersion, new JuristRef(jurist.PublicId, jurist.FullName),
                previous is null ? null : new JuristRef(previous.PublicId, previous.Name));
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException m && AssignmentInsertConflict.IsSlotTaken(m.Number, m.Message))
        {
            return AssignLawyersHandler.Changed(); // a parallel change of the same case went first
        }
    }
}
