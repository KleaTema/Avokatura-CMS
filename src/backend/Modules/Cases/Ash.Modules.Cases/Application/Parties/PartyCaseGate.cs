using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>
/// Who may read or change the parties of a case. Order: no view → 404; CLOSED → 409; not allowed → 403.
/// Change rights: while REGISTERED, the registering office's Protokoll (system permission case.edit_registration);
/// in any open state, whoever holds the case permission (party.add / party.edit / party.delete).
/// </summary>
internal sealed class PartyCaseGate(CasesDbContext db, ICaseAccessService access, ICurrentUser user)
{
    public const string EditRegistration = "case.edit_registration";
    public const string Add = "party.add";
    public const string Edit = "party.edit";
    public const string Delete = "party.delete";

    public async Task<Result<Case>> ForReadAsync(Guid casePublicId, CancellationToken ct)
    {
        var found = await db.Cases.AsNoTracking().SingleOrDefaultAsync(c => c.PublicId == casePublicId, ct);
        return found is not null && await access.CanViewAsync((long)found.Id, ct) ? found : NotFound();
    }

    public async Task<Result<Case>> ForChangeAsync(Guid casePublicId, string casePermission, CancellationToken ct)
    {
        var read = await ForReadAsync(casePublicId, ct);
        if (read.IsFailure)
        {
            return read;
        }

        var c = read.Value;
        if (c.LifecycleStatus == Case.Closed)
        {
            return Error.Conflict("case.closed", "Çështja është mbyllur; palët ndryshohen në shkallën e radhës.");
        }

        var registrationDesk = c.LifecycleStatus == Case.Registered
                               && user.HasPermission(EditRegistration)
                               && user.OfficeId == c.OfficeId;
        if (registrationDesk || await access.HasAsync((long)c.Id, casePermission, ct))
        {
            return c;
        }

        return Error.Forbidden("case.forbidden", "Nuk keni leje për këtë veprim.");
    }

    public static Error NotFound() => Error.NotFound("case.not_found", "Çështja nuk u gjet.");

    public static Error PartyNotFound() => Error.NotFound("party.not_found", "Pala nuk u gjet.");
}
