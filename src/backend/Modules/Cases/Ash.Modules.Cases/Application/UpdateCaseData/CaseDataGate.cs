using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.UpdateCaseData;

/// <summary>
/// The shared checks of the case-data endpoints (task 025), in this order: no view → 404; CLOSED → 409 (nothing of
/// ActionsAllowedWhenClosed is edited here); none of the action's permissions in the effective permissions → 403;
/// stale rowVersion → 409. Returns the TRACKED case; <see cref="SaveAsync"/> increments row_version and saves
/// (the UPDATE checks the row_version it read, so a change committed in between is a 409 too).
/// </summary>
internal sealed class CaseDataGate(CasesDbContext db, ICaseAccessService access)
{
    public async Task<Result<Case>> ForChangeAsync(Guid casePublicId, uint rowVersion, string[] anyOf, CancellationToken ct)
    {
        var c = await db.Cases.SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        if (c.LifecycleStatus == Case.Closed)
        {
            return Error.Conflict("case.closed", "Çështja është mbyllur; të dhënat e saj nuk ndryshohen më.");
        }

        var effective = await access.GetEffectivePermissionsAsync((long)c.Id, ct);
        if (!anyOf.Any(effective.Contains))
        {
            return AssignLawyersHandler.Forbidden();
        }

        if (c.RowVersion != rowVersion)
        {
            return AssignLawyersHandler.Changed();
        }

        return c;
    }

    /// <summary>Increments row_version and saves everything tracked; null on success, the 409 otherwise.</summary>
    public async Task<Error?> SaveAsync(Case c, CancellationToken ct)
    {
        c.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return AssignLawyersHandler.Changed();
        }
    }

    public static Error NoChange() => Error.Validation("case.no_change", "Nuk ka ndryshime.");
}
