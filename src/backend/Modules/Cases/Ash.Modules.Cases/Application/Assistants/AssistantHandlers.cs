using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.UpdateCaseData;
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

namespace Ash.Modules.Cases.Application.Assistants;

/// <summary>
/// Legal assistants of a case (task 026), managed by the primary lawyer (assistant.manage, a case permission).
/// Several assistants per case. TODO(client-q5): several assistants allowed (default).
/// Adding or removing an assistant is the lawyer organising the team, not data entry: FirstDataEntry is not called.
/// </summary>
internal static class AssistantRules
{
    public const string Permission = "assistant.manage";
    public const string Role = "LEGAL_ASSISTANT";

    public static Error AlreadyOnCase() =>
        Error.ValidationFields(AssistantValidator.InvalidMessage, new Dictionary<string, string[]>
        {
            ["userPublicId"] = ["Ky përdorues ka tashmë një rol në këtë çështje."]
        });

    public static Error NotAssistant() =>
        Error.NotFound("assistant.not_found", "Ky përdorues nuk është ndihmës ligjor në këtë çështje.");

    /// <summary>The users with a current role in the case (any role), by internal id.</summary>
    public static async Task<HashSet<long>> OnCaseAsync(CasesDbContext db, ulong caseId, CancellationToken ct) =>
        (await db.CaseAssignments.AsNoTracking()
            .Where(a => a.CaseId == caseId && a.EndedAt == null)
            .Select(a => a.UserId)
            .ToListAsync(ct))
        .Select(id => (long)id)
        .ToHashSet();
}

/// <summary>
/// GET assignable-assistants: active LEGAL_ASSISTANTs of the case's office who have no current role in this case,
/// with how many open cases each assists (one grouped query), least loaded first, then by name.
/// </summary>
internal sealed class AssignableAssistantsHandler(CasesDbContext db, ICaseAccessService access, IUserDirectory users)
{
    public async Task<Result<IReadOnlyList<AssignableAssistant>>> Handle(Guid casePublicId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking()
            .Where(x => x.PublicId == casePublicId)
            .Select(x => new { x.Id, x.OfficeId })
            .SingleOrDefaultAsync(ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        if (!(await access.GetEffectivePermissionsAsync((long)c.Id, ct)).Contains(AssistantRules.Permission))
        {
            return AssignLawyersHandler.Forbidden();
        }

        var onCase = await AssistantRules.OnCaseAsync(db, c.Id, ct);
        var candidates = (await users.GetOfficeUsersByRoleAsync(c.OfficeId, AssistantRules.Role, ct))
            .Where(u => !onCase.Contains(u.Id))
            .ToList();

        var ids = candidates.Select(u => (uint)u.Id).ToList();
        var workload = await (
                from a in db.CaseAssignments.AsNoTracking()
                join x in db.Cases.AsNoTracking() on a.CaseId equals x.Id
                where a.EndedAt == null && a.CaseRole == CaseAssignmentRow.LegalAssistant && ids.Contains(a.UserId)
                      && x.LifecycleStatus != Case.Closed
                group a by a.UserId into g
                select new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => (long)r.UserId, r => r.Count, ct);

        return candidates
            .Select(u => new AssignableAssistant(u.PublicId, u.FullName, workload.GetValueOrDefault(u.Id)))
            .OrderBy(x => x.ActiveAssignments)
            .ThenBy(x => x.FullName, OfficeLawyers.NameOrder)
            .ToList();
    }
}

/// <summary>
/// POST assistants: the user must be an active LEGAL_ASSISTANT of the case's office without a current role in this
/// case (checked before the unique key; a parallel request that went first → 409). ASSISTANT_ADDED, AssistantAssigned.
/// </summary>
internal sealed class AddAssistantHandler(
    CasesDbContext db, CaseDataGate gate, IUserDirectory users, IHistoryWriter history, IAuditLog audit,
    IEventPublisher events, ICurrentUser user, IClock clock)
{
    public async Task<Result<AssistantChangeResponse>> Handle(Guid casePublicId, AddAssistantRequest req, CancellationToken ct)
    {
        var invalid = AssistantValidator.ValidateAdd(req);
        if (invalid is not null)
        {
            return invalid;
        }

        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, [AssistantRules.Permission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var userId = (await users.GetIdsAsync([req.UserPublicId!.Value], ct)).GetValueOrDefault(req.UserPublicId.Value);
        if (userId != 0 && (await AssistantRules.OnCaseAsync(db, c.Id, ct)).Contains(userId))
        {
            return AssistantRules.AlreadyOnCase();
        }

        var assistant = (await users.GetOfficeUsersByRoleAsync(c.OfficeId, AssistantRules.Role, ct))
            .FirstOrDefault(u => u.PublicId == req.UserPublicId);
        if (assistant is null)
        {
            return Error.ValidationFields(AssistantValidator.InvalidMessage, new Dictionary<string, string[]>
            {
                ["userPublicId"] = ["Ky përdorues nuk mund të shtohet si ndihmës ligjor në këtë çështje."]
            });
        }

        db.CaseAssignments.Add(new CaseAssignmentRow
        {
            CaseId = c.Id,
            UserId = (uint)assistant.Id,
            CaseRole = CaseAssignmentRow.LegalAssistant,
            AssignedBy = (uint)user.UserId,
            AssignedAt = clock.UtcNow
        });
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.AssistantAdded,
            $"Ndihmësi ligjor u shtua: {assistant.FullName}.",
            @new: new { Name = assistant.FullName, UserPublicId = assistant.PublicId });

        Error? conflict;
        try
        {
            conflict = await gate.SaveAsync(c, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException m && AssignmentInsertConflict.IsSlotTaken(m.Number, m.Message))
        {
            conflict = Error.Conflict("assistant.already_on_case", "Ky përdorues ka tashmë një rol në këtë çështje.");
        }

        if (conflict is not null)
        {
            return conflict;
        }

        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id,
            details: new { Action = "ADD_ASSISTANT", UserPublicId = assistant.PublicId });
        await events.PublishAsync(new AssistantAssigned(c.PublicId, assistant.PublicId), ct);
        return new AssistantChangeResponse(c.RowVersion, new AssistantRef(assistant.PublicId, assistant.FullName));
    }
}

/// <summary>
/// DELETE assistants/{userPublicId}: the current LEGAL_ASSISTANT row is ENDED (ended_at, ended_by, REMOVED), never
/// deleted; the assistant loses access at once. ASSISTANT_REMOVED, AssistantRemoved.
/// </summary>
internal sealed class RemoveAssistantHandler(
    CasesDbContext db, CaseDataGate gate, IUserDirectory users, IHistoryWriter history, IAuditLog audit,
    IEventPublisher events, ICurrentUser user, IClock clock)
{
    public async Task<Result<AssistantChangeResponse>> Handle(
        Guid casePublicId, Guid assistantPublicId, uint? rowVersion, CancellationToken ct)
    {
        var invalid = AssistantValidator.ValidateRemove(rowVersion);
        if (invalid is not null)
        {
            return invalid;
        }

        var found = await gate.ForChangeAsync(casePublicId, rowVersion!.Value, [AssistantRules.Permission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var userId = (await users.GetIdsAsync([assistantPublicId], ct)).GetValueOrDefault(assistantPublicId);
        var row = userId == 0
            ? null
            : await db.CaseAssignments.SingleOrDefaultAsync(a => a.CaseId == c.Id && a.UserId == (uint)userId
                && a.CaseRole == CaseAssignmentRow.LegalAssistant && a.EndedAt == null, ct);
        if (row is null)
        {
            return AssistantRules.NotAssistant();
        }

        var name = (await users.GetRefsAsync([userId], ct))[userId].Name;
        row.EndedAt = clock.UtcNow;
        row.EndedBy = (uint)user.UserId;
        row.EndReason = CaseAssignmentRow.Removed;
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.AssistantRemoved,
            $"Ndihmësi ligjor u hoq: {name}.",
            old: new { Name = name, UserPublicId = assistantPublicId });
        if (await gate.SaveAsync(c, ct) is { } conflict)
        {
            return conflict;
        }

        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id,
            details: new { Action = "REMOVE_ASSISTANT", UserPublicId = assistantPublicId });
        await events.PublishAsync(new AssistantRemoved(c.PublicId, assistantPublicId), ct);
        return new AssistantChangeResponse(c.RowVersion, new AssistantRef(assistantPublicId, name));
    }
}
