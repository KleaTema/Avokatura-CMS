using Ash.Modules.Cases.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>Duplicate rules against the parties already in the case (deleted ones do not count).</summary>
internal static class PartyRules
{
    public const string ImmutableKindMessage = "Për të ndryshuar llojin ose institucionin, fshini palën dhe shtoni një të re.";

    /// <summary>
    /// The same institution may be a party once; a NATURAL/LEGAL party once per role (name compared case- and
    /// accent-insensitively). <paramref name="exceptPartyId"/> is the party being edited.
    /// </summary>
    public static async Task CheckDuplicatesAsync(
        CasesDbContext db, ulong caseId, byte roleId, string kind, uint? institutionId, string displayName,
        ulong? exceptPartyId, FieldErrors errors, CancellationToken ct)
    {
        var existing = await db.CaseParties.AsNoTracking()
            .Where(p => p.CaseId == caseId && p.DeletedAt == null && (exceptPartyId == null || p.Id != exceptPartyId))
            .Select(p => new { p.PartyRoleId, p.PartyKind, p.InstitutionId, p.DisplayName })
            .ToListAsync(ct);

        if (kind == CaseParty.Institution)
        {
            if (existing.Any(p => p.InstitutionId == institutionId))
            {
                errors.Add("institutionPublicId", "Ky institucion është tashmë palë në këtë çështje.");
            }

            return;
        }

        var name = PartyValidator.NormalizeName(displayName);
        if (existing.Any(p => p.PartyKind != CaseParty.Institution && p.PartyRoleId == roleId
                              && PartyValidator.NormalizeName(p.DisplayName) == name))
        {
            errors.Add("displayName", "Kjo palë me këtë rol është tashmë në çështje.");
        }
    }
}
