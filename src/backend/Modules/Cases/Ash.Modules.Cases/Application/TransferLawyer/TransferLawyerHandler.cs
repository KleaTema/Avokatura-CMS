using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
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

namespace Ash.Modules.Cases.Application.TransferLawyer;

/// <summary>
/// The Kryetar changes the lawyers of an ACTIVE case: replaces the primary (TRANSFERRED — the "Transferuar si çështje"
/// statistic), and/or sets, replaces or removes the secondary (REMOVED). Combinations: the secondary moves up to primary
/// (the slot is then empty unless set), the old primary becomes the secondary. Rows are never deleted: changed rows are
/// ENDED first, then the new rows inserted (the unique slots allow one current primary/secondary and one role per user).
/// Order of checks: fields → no view 404 → CLOSED 409 → REGISTERED 409 → case.transfer missing 403 → stale rowVersion
/// 409 → people not offered for this case / no change / primary = secondary 400.
/// Legal assistants stay. TODO(client-q-assistants-on-transfer): default keep; the new primary may remove them (task 026).
/// TODO(task-040, chat): move the old lawyer's personal reminders for this case; remove the old lawyer from the case chat.
/// </summary>
internal sealed class TransferLawyerHandler(
    CasesDbContext db,
    ICaseAccessService access,
    OfficeLawyers lawyers,
    IUserDirectory users,
    IHistoryWriter history,
    IAuditLog audit,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public const string TransferPermission = "case.transfer";

    private sealed record Holder(ulong RowId, long UserId, Guid PublicId, string Name);

    public async Task<Result<TransferLawyerResponse>> Handle(Guid casePublicId, TransferLawyerRequest req, CancellationToken ct)
    {
        var invalid = TransferLawyerValidator.Validate(req, out var secondaryAction);
        if (invalid is not null)
        {
            return invalid;
        }

        var c = await db.Cases.SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        if (c.LifecycleStatus == Case.Closed)
        {
            return Error.Conflict("case.closed", "Çështja është mbyllur.");
        }

        // the state rule before the permission: case.transfer exists only while ACTIVE (CaseAccessService.ApplyStateRules)
        if (c.LifecycleStatus == Case.Registered)
        {
            return NotAssigned();
        }

        if (!(await access.GetEffectivePermissionsAsync((long)c.Id, ct)).Contains(TransferPermission))
        {
            return AssignLawyersHandler.Forbidden();
        }

        if (c.RowVersion != req.RowVersion)
        {
            return AssignLawyersHandler.Changed();
        }

        // Who holds the slots now (an ACTIVE case always has its primary)
        var (currentPrimary, currentSecondary) = await CurrentLawyersAsync(c.Id, ct);
        if (currentPrimary is null)
        {
            return NotAssigned();
        }

        // The new people: only those the picker offers for this case
        var candidates = await lawyers.ListAsync(c.OfficeId, includeSelf: true, ct);
        var errors = new FieldErrors();
        LawyerCandidate? newPrimary = null;
        if (req.NewPrimaryUserPublicId is { } p)
        {
            newPrimary = candidates.Find(x => x.PublicId == p);
            if (newPrimary is null)
            {
                errors.Add("newPrimaryUserPublicId", "Ky avokat nuk mund të caktohet në këtë çështje.");
            }
            else if (newPrimary.UserId == currentPrimary.UserId)
            {
                errors.Add("newPrimaryUserPublicId", "Ky është avokati primar aktual. Zgjidhni një avokat tjetër.");
            }
        }

        LawyerCandidate? setSecondary = null;
        if (secondaryAction == TransferLawyerValidator.Set)
        {
            setSecondary = candidates.Find(x => x.PublicId == req.Secondary!.UserPublicId);
            if (setSecondary is null)
            {
                errors.Add("secondary.userPublicId", "Ky avokat nuk mund të caktohet në këtë çështje.");
            }
        }

        if (errors.Any)
        {
            return errors.ToError(TransferLawyerValidator.InvalidMessage);
        }

        // The result: the final primary and secondary (user ids), and what changes
        var finalPrimaryId = newPrimary?.UserId ?? currentPrimary.UserId;
        long? finalSecondaryId = secondaryAction switch
        {
            TransferLawyerValidator.Set => setSecondary!.UserId,
            TransferLawyerValidator.Remove => null,
            // keep: unless the secondary just moved up to primary — then the slot is empty
            _ => currentSecondary is not null && currentSecondary.UserId != finalPrimaryId ? currentSecondary.UserId : null
        };

        if (finalSecondaryId == finalPrimaryId)
        {
            return Error.ValidationFields(TransferLawyerValidator.InvalidMessage, new Dictionary<string, string[]>
            {
                ["secondary.userPublicId"] = ["Avokati sekondar duhet të jetë i ndryshëm nga avokati primar."]
            });
        }

        var primaryChanged = newPrimary is not null;
        var secondaryChanged = finalSecondaryId != currentSecondary?.UserId;
        if (!primaryChanged && !secondaryChanged)
        {
            return TransferLawyerValidator.NoChange();
        }

        var now = clock.UtcNow;
        var me = (uint)user.UserId;

        // 1. END the rows that change — before any insert, so a moved lawyer frees their slot first. A row a parallel
        //    request already ended is not ended again (ended_at IS NULL): fewer rows than expected → 409.
        var toEnd = new List<(ulong RowId, string Reason)>();
        if (primaryChanged)
        {
            toEnd.Add((currentPrimary.RowId, CaseAssignmentRow.Transferred));
        }

        if (secondaryChanged && currentSecondary is not null)
        {
            toEnd.Add((currentSecondary.RowId, CaseAssignmentRow.Removed));
        }

        try
        {
            foreach (var (rowId, reason) in toEnd)
            {
                var ended = await db.CaseAssignments
                    .Where(a => a.Id == rowId && a.EndedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.EndedAt, now)
                        .SetProperty(a => a.EndedBy, me)
                        .SetProperty(a => a.EndReason, reason), ct);
                if (ended != 1)
                {
                    return AssignLawyersHandler.Changed();
                }
            }

            // 2. Insert the new rows
            if (primaryChanged)
            {
                db.CaseAssignments.Add(NewRow(c.Id, finalPrimaryId, CaseAssignmentRow.PrimaryLawyer, now));
            }

            if (secondaryChanged && finalSecondaryId is { } secondaryId)
            {
                db.CaseAssignments.Add(NewRow(c.Id, secondaryId, CaseAssignmentRow.SecondaryLawyer, now));
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (SlotTaken(ex))
        {
            return AssignLawyersHandler.Changed(); // a parallel change of the same case went first
        }

        // 3. The case: the 30-day reminder restarts for a new primary; row_version (checked by the UPDATE)
        // TODO(client-q6): restarting after a transfer is the default; first_data_entry_at is not touched.
        if (primaryChanged)
        {
            c.AssignedAt = now;
        }

        c.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AssignLawyersHandler.Changed();
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: AssignmentInsertConflict.Deadlock })
        {
            return AssignLawyersHandler.Changed();
        }

        // 4. History, audit and the event — same transaction
        var people = People(candidates, currentPrimary, currentSecondary);
        var finalPrimary = people[finalPrimaryId];
        var finalSecondary = finalSecondaryId is { } fs ? people[fs] : null;
        var reasonText = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
        var dossierId = (long)c.DossierId;
        var caseId = (long)c.Id;

        if (primaryChanged)
        {
            history.Add(dossierId, caseId, HistoryEventType.LawyerTransferred,
                $"Avokati primar u ndryshua: {currentPrimary.Name} → {finalPrimary.Name}.",
                old: new { currentPrimary.Name, UserPublicId = currentPrimary.PublicId },
                @new: new { finalPrimary.Name, UserPublicId = finalPrimary.PublicId, Reason = reasonText });
        }

        if (secondaryChanged)
        {
            if (finalSecondary is not null)
            {
                history.Add(dossierId, caseId, HistoryEventType.SecondaryAssigned,
                    currentSecondary is null
                        ? $"Avokati sekondar u caktua: {finalSecondary.Name}."
                        : $"Avokati sekondar u ndryshua: {currentSecondary.Name} → {finalSecondary.Name}.",
                    old: currentSecondary is null ? null : new { currentSecondary.Name, UserPublicId = currentSecondary.PublicId },
                    @new: new { finalSecondary.Name, UserPublicId = finalSecondary.PublicId });
            }
            else if (currentSecondary is not null)
            {
                history.Add(dossierId, caseId, HistoryEventType.SecondaryRemoved,
                    currentSecondary.UserId == finalPrimaryId
                        ? $"{currentSecondary.Name} nuk është më avokat sekondar: u bë avokat primar."
                        : $"Avokati sekondar u hoq: {currentSecondary.Name}.",
                    old: new { currentSecondary.Name, UserPublicId = currentSecondary.PublicId });
            }
        }

        await db.SaveChangesAsync(ct);

        audit.Record(AuditActions.CaseUpdate, "CASE", caseId, caseId, details: new
        {
            Action = "TRANSFER_LAWYER",
            OldPrimaryUserPublicId = currentPrimary.PublicId,
            NewPrimaryUserPublicId = finalPrimary.PublicId,
            OldSecondaryUserPublicId = currentSecondary?.PublicId,
            NewSecondaryUserPublicId = finalSecondary?.PublicId,
            Reason = reasonText
        });

        await events.PublishAsync(new LawyerTransferred(c.PublicId, currentPrimary.PublicId, finalPrimary.PublicId,
            currentSecondary?.PublicId, finalSecondary?.PublicId), ct);

        return new TransferLawyerResponse(c.PublicId, new LawyerRef(finalPrimary.PublicId, finalPrimary.Name),
            finalSecondary is null ? null : new LawyerRef(finalSecondary.PublicId, finalSecondary.Name), c.RowVersion);
    }

    private CaseAssignmentRow NewRow(ulong caseId, long userId, string role, DateTime now) => new()
    {
        CaseId = caseId,
        UserId = (uint)userId,
        CaseRole = role,
        AssignedBy = (uint)user.UserId,
        AssignedAt = now
    };

    /// <summary>The current primary and secondary rows with the holders' names (inactive users included).</summary>
    private async Task<(Holder? Primary, Holder? Secondary)> CurrentLawyersAsync(ulong caseId, CancellationToken ct)
    {
        var rows = await db.CaseAssignments.AsNoTracking()
            .Where(a => a.CaseId == caseId && a.EndedAt == null
                        && (a.CaseRole == CaseAssignmentRow.PrimaryLawyer || a.CaseRole == CaseAssignmentRow.SecondaryLawyer))
            .Select(a => new { a.Id, a.UserId, a.CaseRole })
            .ToListAsync(ct);
        var refs = await users.GetRefsAsync(rows.Select(r => (long)r.UserId).ToList(), ct);

        Holder? Of(string role) => rows.Where(r => r.CaseRole == role)
            .Select(r => new Holder(r.Id, r.UserId, refs[r.UserId].PublicId, refs[r.UserId].Name))
            .SingleOrDefault();

        return (Of(CaseAssignmentRow.PrimaryLawyer), Of(CaseAssignmentRow.SecondaryLawyer));
    }

    /// <summary>Public id and name per user id of everyone before and after (names are a snapshot for the history).</summary>
    private static Dictionary<long, LawyerRef> People(IEnumerable<LawyerCandidate> candidates, Holder primary, Holder? secondary)
    {
        var people = candidates.ToDictionary(x => x.UserId, x => new LawyerRef(x.PublicId, x.FullName));
        people[primary.UserId] = new LawyerRef(primary.PublicId, primary.Name);
        if (secondary is not null)
        {
            people[secondary.UserId] = new LawyerRef(secondary.PublicId, secondary.Name);
        }

        return people;
    }

    /// <summary>1062 on a lawyer/user slot or 1213, from the insert (wrapped by EF) or the UPDATE (ExecuteUpdate).</summary>
    private static bool SlotTaken(Exception ex) =>
        (ex is DbUpdateException { InnerException: MySqlException wrapped } ? wrapped : ex as MySqlException) is { } m
        && AssignmentInsertConflict.IsSlotTaken(m.Number, m.Message);

    private static Error NotAssigned() =>
        Error.Conflict("case.not_assigned", "Çështja nuk ka ende avokat. Përdorni caktimin e avokatit.");
}
