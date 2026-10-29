using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Infrastructure;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>
/// Format rules of one party (no database), shared by registration (task 016) and the party endpoints (task 017).
/// Roles and institutions are checked against the lookups by <see cref="PartyResolver"/>.
/// </summary>
internal static partial class PartyValidator
{
    public const int MaxNameLength = 300;
    public const int MaxIdentifierLength = 30;

    private static readonly string[] Kinds = [CaseParty.Natural, CaseParty.Legal, CaseParty.Institution];

    /// <summary>
    /// Adds the errors of <paramref name="p"/> under <paramref name="prefix"/> ("" or "parties[1].").
    /// Returns the kind when it is valid, otherwise null.
    /// </summary>
    public static string? ValidateFormat(PartyRequest? p, string prefix, FieldErrors errors)
    {
        if (p is null)
        {
            errors.Add(prefix.TrimEnd('.'), "Pala nuk është e vlefshme.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(p.PartyRoleCode))
        {
            errors.Add($"{prefix}partyRoleCode", "Roli i palës është i detyrueshëm.");
        }

        var kind = p.Kind?.Trim();
        if (kind is null || !Kinds.Contains(kind))
        {
            errors.Add($"{prefix}kind", "Lloji i palës duhet të jetë NATURAL, LEGAL ose INSTITUTION.");
            return null;
        }

        if (kind == CaseParty.Institution)
        {
            if (p.InstitutionPublicId is null)
            {
                errors.Add($"{prefix}institutionPublicId", "Zgjidhni institucionin nga lista.");
            }
        }
        else
        {
            if (p.InstitutionPublicId is not null)
            {
                errors.Add($"{prefix}institutionPublicId", "Institucioni zgjidhet vetëm për palë të llojit institucion.");
            }

            if (string.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.Trim().Length > MaxNameLength)
            {
                errors.Add($"{prefix}displayName", $"Emri i palës është i detyrueshëm (deri në {MaxNameLength} karaktere).");
            }
        }

        var identifier = CleanIdentifier(kind, p.Identifier);
        if (identifier is { Length: > MaxIdentifierLength })
        {
            errors.Add($"{prefix}identifier", $"Identifikuesi mund të ketë deri në {MaxIdentifierLength} karaktere.");
        }
        else if (kind == CaseParty.Legal && identifier is not null && !NiptRegex().IsMatch(identifier))
        {
            errors.Add($"{prefix}identifier", "NIPT-i duhet të ketë formatin: një shkronjë, 8 shifra, një shkronjë (p.sh. K12345678A).");
        }

        return kind;
    }

    /// <summary>The identifier as stored: trimmed; for a legal entity also upper-case (NIPT). Blank = none.</summary>
    public static string? CleanIdentifier(string? kind, string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var trimmed = identifier.Trim();
        return kind?.Trim() == CaseParty.Legal ? trimmed.ToUpperInvariant() : trimmed;
    }

    /// <summary>Case- and accent-insensitive, single spaces: "Agim  Hoxha" = "agim hoxha" = "Agïm Hoxha".</summary>
    public static string NormalizeName(string name)
    {
        var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var letters = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return string.Join(' ', new string(letters.ToArray()).ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public const string DuplicateNameMessage = "E njëjta palë me të njëjtin rol është shtuar dy herë.";
    public const string DuplicateInstitutionMessage = "I njëjti institucion është shtuar dy herë.";

    // [0-9], not \d: \d also matches non-ASCII digits
    [GeneratedRegex("^[A-Z][0-9]{8}[A-Z]$")]
    private static partial Regex NiptRegex();
}
