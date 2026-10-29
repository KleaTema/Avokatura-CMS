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

/// <summary>
/// Adds a party to a case, in the request transaction: the case_parties row, PARTY_ADDED, and for an institution
/// party a MONITORING link (if the institution has no active link on the case yet) with INSTITUTION_LINKED and the
/// InstitutionLinked event. Used by registration (task 016) and by the party endpoints (task 017).
/// </summary>
internal sealed class PartyWriter(
    CasesDbContext db, IHistoryWriter history, IEventPublisher events, ICurrentUser user, IClock clock)
{
    public async Task AddAsync(Case target, NewParty party, CancellationToken ct)
    {
        var userId = (uint)user.UserId;
        var now = clock.UtcNow;
        // institution parties keep the institution's current name as a snapshot
        var name = party.Institution?.Name ?? party.DisplayName;

        db.CaseParties.Add(new CaseParty
        {
            CaseId = target.Id,
            PartyRoleId = party.Role.Id,
            PartyKind = party.Kind,
            InstitutionId = party.Institution is { } i ? (uint)i.Id : null,
            DisplayName = name,
            Identifier = party.Identifier,
            CreatedBy = userId,
            CreatedAt = now
        });
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
    }

    private async Task<bool> HasActiveLinkAsync(ulong caseId, uint institutionId, CancellationToken ct) =>
        db.CaseInstitutions.Local.Any(x => x.CaseId == caseId && x.InstitutionId == institutionId && x.EndedAt == null)
        || await db.CaseInstitutions.AnyAsync(x => x.CaseId == caseId && x.InstitutionId == institutionId && x.EndedAt == null, ct);
}
