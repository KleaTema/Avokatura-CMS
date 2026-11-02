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
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>
/// The Kryetar assigns the primary (and optionally the secondary) lawyer of a REGISTERED case and may set its priority;
/// the case becomes ACTIVE and assigned_at starts the 30-day reminder. Order of checks: fields → no view 404 →
/// case.assign_lawyer missing from the effective permissions 403 → not REGISTERED 409 → stale rowVersion 409 →
/// lawyers not offered for this case 400 → priority without case.set_priority_any 403.
/// </summary>
internal sealed class AssignLawyersHandler(
    CasesDbContext db,
    ICaseAccessService access,
    OfficeLawyers lawyers,
    IInstitutionDirectory institutions,
    IHistoryWriter history,
    IAuditLog audit,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public const string SetPriorityPermission = "case.set_priority_any";

    /// <summary>The Albanian names of the priorities, for history summaries (also used by SetPriorityHandler).</summary>
    internal static readonly Dictionary<string, string> PriorityNames = new()
    {
        ["NONE"] = "Pa prioritet",
        ["LOW"] = "I ulët",
        ["MEDIUM"] = "Mesatar",
        ["HIGH"] = "I lartë",
    };

    public async Task<Result<AssignLawyersResponse>> Handle(Guid casePublicId, AssignLawyersRequest req, CancellationToken ct)
    {
        var invalid = AssignLawyersValidator.Validate(req);
        if (invalid is not null)
        {
            return invalid;
        }

        var c = await db.Cases.SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return NotFound();
        }

        // A CLOSED case keeps no write action (ActionsAllowedWhenClosed), so it is refused here with 403.
        var effective = await access.GetEffectivePermissionsAsync((long)c.Id, ct);
        if (!effective.Contains(OfficeLawyers.AssignPermission))
        {
            return Forbidden();
        }

        if (c.LifecycleStatus != Case.Registered)
        {
            return AlreadyAssigned();
        }

        if (c.RowVersion != req.RowVersion)
        {
            return Changed();
        }

        // Only the people the picker offers for this case: the office's active lawyers and the Kryetar himself.
        var candidates = await lawyers.ListAsync(c.OfficeId, includeSelf: true, ct);
        var primary = candidates.Find(x => x.PublicId == req.PrimaryUserPublicId);
        var secondary = req.SecondaryUserPublicId is { } s ? candidates.Find(x => x.PublicId == s) : null;
        var errors = new FieldErrors();
        if (primary is null)
        {
            errors.Add("primaryUserPublicId", "Ky avokat nuk mund të caktohet në këtë çështje.");
        }

        if (req.SecondaryUserPublicId is not null && secondary is null)
        {
            errors.Add("secondaryUserPublicId", "Ky avokat nuk mund të caktohet në këtë çështje.");
        }

        if (errors.Any)
        {
            return errors.ToError(AssignLawyersValidator.InvalidMessage);
        }

        if (req.Priority is not null && !effective.Contains(SetPriorityPermission))
        {
            return Forbidden();
        }

        var now = clock.UtcNow;

        // 1. The assignment rows first: a parallel assignment that inserted first holds the slots, so this insert
        //    waits for it and then fails on the unique key → 409, never 500.
        db.CaseAssignments.Add(NewAssignment(c, primary!, CaseAssignmentRow.PrimaryLawyer, now));
        if (secondary is not null)
        {
            db.CaseAssignments.Add(NewAssignment(c, secondary, CaseAssignmentRow.SecondaryLawyer, now));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysql
                                           && AssignmentInsertConflict.IsSlotTaken(mysql.Number, mysql.Message))
        {
            return AlreadyAssigned();
        }

        // 2. The case: ACTIVE, assigned_at, priority, row_version (the UPDATE checks the row_version it read)
        var oldPriority = c.Priority;
        c.LifecycleStatus = Case.Active;
        c.AssignedAt = now;
        c.Priority = req.Priority ?? c.Priority;
        c.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed();
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: AssignmentInsertConflict.Deadlock })
        {
            return AlreadyAssigned(); // lost a deadlock against a parallel assignment, which went through
        }

        // 3. History, audit and the event — same transaction
        var dossierId = (long)c.DossierId;
        var caseId = (long)c.Id;
        history.Add(dossierId, caseId, HistoryEventType.LawyerAssigned,
            $"Avokati primar u caktua: {primary!.FullName}.",
            @new: new { Name = primary.FullName, UserPublicId = primary.PublicId });
        if (secondary is not null)
        {
            history.Add(dossierId, caseId, HistoryEventType.SecondaryAssigned,
                $"Avokati sekondar u caktua: {secondary.FullName}.",
                @new: new { Name = secondary.FullName, UserPublicId = secondary.PublicId });
        }

        if (c.Priority != oldPriority)
        {
            history.Add(dossierId, caseId, HistoryEventType.PriorityChanged,
                $"Prioriteti u ndryshua: {PriorityNames[oldPriority]} → {PriorityNames[c.Priority]}.",
                old: new { Priority = oldPriority }, @new: new { c.Priority });
        }

        await db.SaveChangesAsync(ct);

        audit.Record(AuditActions.CaseUpdate, "CASE", caseId, caseId, details: new
        {
            Action = "ASSIGN_LAWYERS",
            PrimaryUserPublicId = primary.PublicId,
            SecondaryUserPublicId = secondary?.PublicId,
            Priority = c.Priority != oldPriority ? c.Priority : null
        });

        await events.PublishAsync(new LawyerAssigned(c.PublicId, primary.PublicId, secondary?.PublicId,
            await LinkedInstitutionsAsync(c.Id, ct)), ct);

        return new AssignLawyersResponse(c.PublicId, c.LifecycleStatus, c.Priority, now, c.RowVersion);
    }

    private CaseAssignmentRow NewAssignment(Case c, LawyerCandidate lawyer, string role, DateTime now) => new()
    {
        CaseId = c.Id,
        UserId = (uint)lawyer.UserId,
        CaseRole = role,
        AssignedBy = (uint)user.UserId,
        AssignedAt = now
    };

    /// <summary>Public ids of the institutions with an active link (MONITORING or DELEGATED) to the case.</summary>
    private async Task<IReadOnlyList<Guid>> LinkedInstitutionsAsync(ulong caseId, CancellationToken ct)
    {
        var ids = await db.CaseInstitutions.AsNoTracking()
            .Where(x => x.CaseId == caseId && x.EndedAt == null)
            .Select(x => (long)x.InstitutionId)
            .ToListAsync(ct);
        if (ids.Count == 0)
        {
            return [];
        }

        return (await institutions.GetByIdsAsync(ids, ct)).Select(i => i.PublicId).ToList();
    }

    internal static Error NotFound() => Error.NotFound("case.not_found", "Çështja nuk u gjet.");

    internal static Error Forbidden() => Error.Forbidden("case.forbidden", "Nuk keni leje për këtë veprim.");

    internal static Error AlreadyAssigned() =>
        Error.Conflict("case.already_assigned", "Çështja ka tashmë avokat. Për ndryshim përdorni transferimin.");

    internal static Error Changed() => Error.Conflict("case.changed", "Të dhënat u ndryshuan nga një përdorues tjetër.");
}

/// <summary>
/// A failed assignment insert caused by a parallel assignment of the same case (never a 500): 1062 on
/// uq_ca_lawyer_slot (the case already has a current primary/secondary) or uq_ca_user_slot (the user already has a
/// role in the case), or 1213 when the two requests deadlock. Any other database error is not mapped.
/// </summary>
public static class AssignmentInsertConflict
{
    public const int DuplicateKey = 1062;
    public const int Deadlock = 1213;

    public static bool IsSlotTaken(int mySqlErrorNumber, string message) =>
        mySqlErrorNumber switch
        {
            DuplicateKey => message.Contains("uq_ca_lawyer_slot", StringComparison.Ordinal)
                            || message.Contains("uq_ca_user_slot", StringComparison.Ordinal),
            Deadlock => true,
            _ => false
        };
}
