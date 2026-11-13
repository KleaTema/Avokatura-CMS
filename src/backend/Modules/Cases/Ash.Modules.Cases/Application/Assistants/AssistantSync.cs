using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Assistants;

/// <summary>
/// Keeps the LEGAL_ASSISTANT rows of case_assignments in step with the assistant links (task 053e, client decision C):
/// an assistant linked to a lawyer holds a current row — marked with the link (assistant_link_id) — on EVERY case where
/// that lawyer is the CURRENT primary, and on no other case through that link. The access code (v_case_access,
/// CaseScopeQuery) is unchanged: it simply sees assignment rows.
/// <para>
/// One rule, <see cref="SyncCaseAsync"/>: the case's wanted links (active links of its current primary) against its
/// link-made rows. Callers: linking / unlinking (every case of the lawyer), LawyerAssigned and LawyerTransferred (that
/// case), and the nightly reconcile (every case that has a link row or a linked primary). Idempotent: running it again
/// changes nothing. A closed case keeps its rows, like every other assignment.
/// </para>
/// A new row is not added when the assistant already holds another current role in the case (one role per user and
/// case, uq_ca_user_slot). Each change writes ASSISTANT_ADDED / _REMOVED on the case and publishes AssistantAssigned /
/// AssistantRemoved (the chat follows); the assistant is told once per link, not per case.
/// </summary>
internal sealed class AssistantSync(
    CasesDbContext db,
    IHistoryWriter history,
    IEventPublisher events,
    IUserDirectory users,
    ICurrentUser user,
    IClock clock)
{
    /// <summary>Brings one case in line; returns how many rows were added + ended.</summary>
    public async Task<int> SyncCaseAsync(ulong caseId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null)
        {
            return 0;
        }

        var primary = await db.CaseAssignments.AsNoTracking()
            .Where(a => a.CaseId == caseId && a.EndedAt == null && a.CaseRole == CaseAssignmentRow.PrimaryLawyer)
            .Select(a => (uint?)a.UserId)
            .SingleOrDefaultAsync(ct);
        var wanted = primary is { } lawyer
            ? await db.AssistantLinks.AsNoTracking().Where(l => l.LawyerUserId == lawyer && l.EndedAt == null).ToListAsync(ct)
            : [];
        var current = await db.CaseAssignments
            .Where(a => a.CaseId == caseId && a.EndedAt == null)
            .ToListAsync(ct);
        var linkRows = current.Where(a => a.AssistantLinkId != null).ToList();
        var wantedIds = wanted.Select(l => l.Id).ToHashSet();

        var changes = 0;
        var now = clock.UtcNow;
        var actor = Actor();
        var names = await users.GetRefsAsync(
            [.. wanted.Select(l => (long)l.AssistantUserId), .. linkRows.Select(a => (long)a.UserId)], ct);

        // rows whose link no longer applies here (unlinked, or the primary changed): ended
        foreach (var row in linkRows.Where(a => !wantedIds.Contains(a.AssistantLinkId!.Value)))
        {
            row.EndedAt = now;
            row.EndedBy = actor;
            row.EndReason = CaseAssignmentRow.Removed;
            var name = names.GetValueOrDefault(row.UserId);
            history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.AssistantRemoved,
                $"Ndihmësi ligjor u hoq: {name?.Name}.", old: new { Assistant = name?.Name });
            await db.SaveChangesAsync(ct);
            if (name is not null)
            {
                await events.PublishAsync(new AssistantRemoved(c.PublicId, name.PublicId), ct);
            }

            changes++;
        }

        // wanted links without their row: added (unless the assistant already has a role in the case)
        var holders = current.Where(a => a.EndedAt == null).Select(a => a.UserId).ToHashSet();
        foreach (var link in wanted.Where(l => !linkRows.Any(a => a.AssistantLinkId == l.Id && a.EndedAt == null)))
        {
            if (holders.Contains(link.AssistantUserId))
            {
                continue;
            }

            db.CaseAssignments.Add(new CaseAssignmentRow
            {
                CaseId = c.Id,
                UserId = link.AssistantUserId,
                CaseRole = CaseAssignmentRow.LegalAssistant,
                AssignedBy = actor ?? link.CreatedBy,
                AssistantLinkId = link.Id,
            });
            holders.Add(link.AssistantUserId);
            var name = names.GetValueOrDefault(link.AssistantUserId);
            history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.AssistantAdded,
                $"Ndihmësi ligjor u shtua: {name?.Name} (i lidhur me avokatin primar).", @new: new { Assistant = name?.Name });
            await db.SaveChangesAsync(ct);
            if (name is not null)
            {
                await events.PublishAsync(new AssistantAssigned(c.PublicId, name.PublicId), ct);
            }

            changes++;
        }

        return changes;
    }

    /// <summary>
    /// After linking or unlinking: every case where the link's lawyer is the current primary, plus every case that
    /// still has a row of that link. Returns the cases the assistant holds through the link afterwards.
    /// </summary>
    public async Task<int> SyncLinkAsync(AssistantLink link, CancellationToken ct)
    {
        var lawyerCases = await db.CaseAssignments.AsNoTracking()
            .Where(a => a.UserId == link.LawyerUserId && a.EndedAt == null && a.CaseRole == CaseAssignmentRow.PrimaryLawyer)
            .Select(a => a.CaseId)
            .ToListAsync(ct);
        var linkCases = await db.CaseAssignments.AsNoTracking()
            .Where(a => a.AssistantLinkId == link.Id && a.EndedAt == null)
            .Select(a => a.CaseId)
            .ToListAsync(ct);
        foreach (var caseId in lawyerCases.Union(linkCases).Order())
        {
            await SyncCaseAsync(caseId, ct);
        }

        return await db.CaseAssignments.CountAsync(a => a.AssistantLinkId == link.Id && a.EndedAt == null, ct);
    }

    /// <summary>
    /// The cases the nightly reconcile looks at: those with a current link row, and those whose current primary has an
    /// active link. Any drift (a row missing, a row left behind) is on one of them.
    /// </summary>
    public async Task<List<ulong>> CasesToReconcileAsync(CancellationToken ct)
    {
        var withRows = db.CaseAssignments.AsNoTracking()
            .Where(a => a.AssistantLinkId != null && a.EndedAt == null)
            .Select(a => a.CaseId);
        var withLinkedPrimary = db.CaseAssignments.AsNoTracking()
            .Where(a => a.EndedAt == null && a.CaseRole == CaseAssignmentRow.PrimaryLawyer
                        && db.AssistantLinks.Any(l => l.LawyerUserId == a.UserId && l.EndedAt == null))
            .Select(a => a.CaseId);
        return await withRows.Union(withLinkedPrimary).OrderBy(id => id).ToListAsync(ct);
    }

    /// <summary>The acting user, or none (the nightly job: the "system").</summary>
    private uint? Actor()
    {
        try
        {
            return (uint)user.UserId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// The case events that change a case's primary lawyer (task 053e): its assistant rows follow at once, in the same
/// transaction. Closing keeps them (like every other assignment).
/// </summary>
internal sealed class AssistantSyncEvents(CasesDbContext db, AssistantSync sync) :
    IEventHandler<LawyerAssigned>,
    IEventHandler<LawyerTransferred>
{
    public Task HandleAsync(LawyerAssigned e, CancellationToken ct = default) => SyncAsync(e.CasePublicId, ct);

    public Task HandleAsync(LawyerTransferred e, CancellationToken ct = default) =>
        e.OldPrimaryUserPublicId == e.NewPrimaryUserPublicId ? Task.CompletedTask : SyncAsync(e.CasePublicId, ct);

    private async Task SyncAsync(Guid casePublicId, CancellationToken ct)
    {
        var caseId = await db.Cases.AsNoTracking().Where(c => c.PublicId == casePublicId).Select(c => (ulong?)c.Id).SingleOrDefaultAsync(ct);
        if (caseId is { } id)
        {
            await sync.SyncCaseAsync(id, ct);
        }
    }
}
