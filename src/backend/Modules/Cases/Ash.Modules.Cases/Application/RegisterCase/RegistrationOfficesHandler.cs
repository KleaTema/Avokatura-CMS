using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.RegisterCase;

public sealed record RegistrationOfficeOption(Guid PublicId, string Name);

/// <summary>
/// "Zyra që e ndjek" (task 053j): the active offices to choose from, and the one covering <c>courtId</c> in
/// office_courts (null = no office covers it: the choice is required).
/// </summary>
public sealed record RegistrationOfficesResponse(IReadOnlyList<RegistrationOfficeOption> Offices, Guid? CourtOfficePublicId);

/// <summary>GET /api/cases/registration/offices — for the registration (case.create) and the continuation (case.link_previous).</summary>
internal sealed class RegistrationOfficesHandler(IOfficeDirectory offices, ICurrentUser user)
{
    public async Task<Result<RegistrationOfficesResponse>> Handle(int? courtId, CancellationToken ct)
    {
        if (!user.HasPermission("case.create") && !user.HasPermission("case.link_previous"))
        {
            return Error.Forbidden("case.forbidden", "Nuk keni leje për këtë veprim.");
        }

        var active = await offices.ListActiveAsync(ct);
        var mapped = courtId is { } id ? await offices.GetByCourtAsync(id, ct) : null;
        return new RegistrationOfficesResponse(
            active.OrderBy(o => o.Name, StringComparer.CurrentCulture).Select(o => new RegistrationOfficeOption(o.PublicId, o.Name)).ToList(),
            mapped?.PublicId);
    }
}
