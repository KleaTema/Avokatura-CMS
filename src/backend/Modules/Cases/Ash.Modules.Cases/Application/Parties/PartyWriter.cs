using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>A validated party: role and institution already resolved and active.</summary>
internal sealed record NewParty(PartyRoleDto Role, string Kind, InstitutionInfo? Institution, string DisplayName, string? Identifier);

/// <summary>What removing a party did to the institution link.</summary>
internal sealed record PartyRemoval(bool LastInstitutionParty, bool InstitutionLinkEnded, string? Warning);

/// <summary>
/// All writes of case parties, in the request transaction, each with its history row: add (with the MONITORING
/// institution link), update, soft delete (optionally ending the link). Used by registration (task 016) and the
/// party endpoints (task 017). Party changes do not touch cases.row_version.
/// </summary>
internal sealed class PartyWriter(
    CasesDbContext db, IHistoryWriter history, IEventPublisher events, IInstitutionDirectory institutions,
    ICurrentUser user, IClock clock)
{
    public const string DelegationKeptWarning = "Institucioni ka delegim në këtë çështje; delegimi nuk ndryshon.";

    public async Task<CaseParty> AddAsync(Case target, NewParty party, CancellationToken ct)
    {
        var userId = (uint)user.UserId;
        var now = clock.UtcNow;
        // institution parties keep the institution's current name as a snapshot
        var name = party.Institution?.Name ?? party.DisplayName;

        var row = new CaseParty
        {
            PublicId = Guid.NewGuid(),
            CaseId = target.Id,
            PartyRoleId = party.Role.Id,
            PartyKind = party.Kind,
            InstitutionId = party.Institution is { } i ? (uint)i.Id : null,
            DisplayName = name,
            Identifier = party.Identifier,
            CreatedBy = userId,
            CreatedAt = now
        };
        db.CaseParties.Add(row);
        history.Add((long)target.DossierId, (long)target.Id, HistoryEventType.PartyAdded,
            $"Pala u shtua: {party.Role.Name} — {name}.",
            @new: new { Role = party.Role.Code, party.Kind, DisplayName = name, party.Identifier });

        var newLink = party.Institution is { } institution && !await HasActiveLinkAsync(target.Id, (uint)institution.Id, ct);
        if (newLink)
        {
            db.CaseInstitutions.Add(new CaseInstitution
            {
                CaseId = target.Id,
                InstitutionId = (uint)party.Institution!.Id,
                LinkMode = CaseInstitution.Monitoring,
                StartedAt = now,
                StartedBy = userId
            });
            history.Add((long)target.DossierId, (long)target.Id, HistoryEventType.InstitutionLinked,
                $"Institucioni {name} u lidh me çështjen (monitorim).",
                @new: new { Institution = name, Mode = CaseInstitution.Monitoring });
        }

        await db.SaveChangesAsync(ct);

        if (newLink)
        {
            await events.PublishAsync(
                new InstitutionLinked(target.PublicId, party.Institution!.PublicId, CaseInstitution.Monitoring), ct);
        }

        return row;
    }

    /// <summary>Role for every kind; name and identifier only for NATURAL/LEGAL (an institution's name is its snapshot).</summary>
    public async Task UpdateAsync(
        Case target, CaseParty party, PartyRoleDto oldRole, PartyRoleDto newRole, string displayName, string? identifier,
        CancellationToken ct)
    {
        var before = new { Role = oldRole.Code, party.DisplayName, party.Identifier };
        var after = new { Role = newRole.Code, DisplayName = displayName, Identifier = identifier };
        if (before.Equals(after)) // same anonymous type: value equality
        {
            return; // nothing changed: no history row
        }

        party.PartyRoleId = newRole.Id;
        party.DisplayName = displayName;
        party.Identifier = identifier;
        history.Add((long)target.DossierId, (long)target.Id, HistoryEventType.PartyUpdated,
            $"Pala u ndryshua: {newRole.Name} — {displayName}.", old: before, @new: after);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Soft delete. When it was the institution's last party in the case: a MONITORING link is ended only when
    /// <paramref name="endInstitutionLink"/> is true; a DELEGATED link is never ended here (warning).
    /// </summary>
    public async Task<PartyRemoval> RemoveAsync(
        Case target, CaseParty party, PartyRoleDto role, bool? endInstitutionLink, CancellationToken ct)
    {
        var userId = (uint)user.UserId;
        var now = clock.UtcNow;
        party.DeletedAt = now;
        party.DeletedBy = userId;
        history.Add((long)target.DossierId, (long)target.Id, HistoryEventType.PartyRemoved,
            $"Pala u hoq: {role.Name} — {party.DisplayName}.",
            old: new { Role = role.Code, party.PartyKind, party.DisplayName, party.Identifier });
        await db.SaveChangesAsync(ct);

        if (party.InstitutionId is not { } institutionId
            || await db.CaseParties.AnyAsync(p => p.CaseId == target.Id && p.InstitutionId == institutionId && p.DeletedAt == null, ct))
        {
            return new PartyRemoval(false, false, null);
        }

        var link = await db.CaseInstitutions.SingleOrDefaultAsync(
            l => l.CaseId == target.Id && l.InstitutionId == institutionId && l.EndedAt == null, ct);
        if (link is null)
        {
            return new PartyRemoval(true, false, null);
        }

        if (link.LinkMode == CaseInstitution.Delegated)
        {
            return new PartyRemoval(true, false, DelegationKeptWarning);
        }

        if (endInstitutionLink != true)
        {
            return new PartyRemoval(true, false, null); // the UI asks the user and may call again with the flag
        }

        link.EndedAt = now;
        link.EndedBy = userId;
        history.Add((long)target.DossierId, (long)target.Id, HistoryEventType.InstitutionUnlinked,
            $"Lidhja e institucionit {party.DisplayName} me çështjen u mbyll (monitorim).",
            old: new { Institution = party.DisplayName, Mode = link.LinkMode });
        await db.SaveChangesAsync(ct);

        var institution = (await institutions.GetByIdsAsync([institutionId], ct)).Single();
        await events.PublishAsync(new InstitutionUnlinked(target.PublicId, institution.PublicId, link.LinkMode), ct);
        return new PartyRemoval(true, true, null);
    }

    private async Task<bool> HasActiveLinkAsync(ulong caseId, uint institutionId, CancellationToken ct) =>
        db.CaseInstitutions.Local.Any(x => x.CaseId == caseId && x.InstitutionId == institutionId && x.EndedAt == null)
        || await db.CaseInstitutions.AnyAsync(x => x.CaseId == caseId && x.InstitutionId == institutionId && x.EndedAt == null, ct);
}
