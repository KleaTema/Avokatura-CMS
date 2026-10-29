using Ash.Modules.Cases.Application.RegisterCase;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>Adds a party; an institution party gets a MONITORING link unless one is already active (PartyWriter).</summary>
internal sealed class AddPartyHandler(
    Infrastructure.CasesDbContext db, PartyCaseGate gate, PartyResolver resolver, PartyWriter writer, PartyViews views)
{
    public async Task<Result<PartyResponse>> Handle(Guid casePublicId, PartyRequest req, CancellationToken ct)
    {
        var c = await gate.ForChangeAsync(casePublicId, PartyCaseGate.Add, ct);
        if (c.IsFailure)
        {
            return c.Error!;
        }

        var errors = new FieldErrors();
        var kind = PartyValidator.ValidateFormat(req, string.Empty, errors);
        var party = kind is null ? null : await resolver.ResolveAsync(req, kind, string.Empty, errors, ct);
        if (party is not null)
        {
            await PartyRules.CheckDuplicatesAsync(db, c.Value.Id, party.Role.Id, party.Kind,
                party.Institution is { } i ? (uint)i.Id : null, party.DisplayName, null, errors, ct);
        }

        if (errors.Any || party is null)
        {
            return errors.ToError("Të dhënat e palës nuk janë të vlefshme.");
        }

        var row = await writer.AddAsync(c.Value, party, ct);
        return await views.ToResponseAsync(row, ct);
    }
}
