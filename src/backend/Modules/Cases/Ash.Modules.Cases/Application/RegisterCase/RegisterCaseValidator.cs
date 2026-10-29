using System.Globalization;
using System.Text.RegularExpressions;
using Ash.Modules.Cases.Application.Parties;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Cases.Application.RegisterCase;

/// <summary>
/// Checks that need no database: required fields, lengths and dates. Keys are the camelCase request paths
/// ("firstHearing.at") so the UI can show each message next to its field.
/// </summary>
internal static partial class RegisterCaseValidator
{
    public const int MaxNumberLength = 50;
    public const int MaxObjectLength = 4000;
    public const int MaxOtherTypeLength = 200;

    public const string JudgeMissingHint = "Nëse gjyqtari mungon në listë, kërkoni administratorit ta shtojë.";

    /// <summary>
    /// Validates the request; returns the hearing time in UTC when it could be read. <paramref name="withParties"/>
    /// is false for the next level (task 018), whose parties are copied from the previous level.
    /// </summary>
    public static DateTime? Validate(RegisterCaseRequest req, DateOnly todayInTirane, FieldErrors errors, bool withParties = true)
    {
        Required(errors, "summonsNumber", req.SummonsNumber, MaxNumberLength, "Numri i fletëthirrjes");
        Required(errors, "actNumber", req.ActNumber, MaxNumberLength, "Numri i aktit");
        Required(errors, "objectText", req.ObjectText, MaxObjectLength, "Objekti");

        if (string.IsNullOrWhiteSpace(req.CourtLevelCode))
        {
            errors.Add("courtLevelCode", "Shkalla e gjykimit është e detyrueshme.");
        }

        if (req.CourtId is null)
        {
            errors.Add("courtId", "Gjykata është e detyrueshme.");
        }

        if (req.JudgeId is null)
        {
            errors.Add("judgeId", $"Gjyqtari është i detyrueshëm. {JudgeMissingHint}");
        }

        // Calendar dates: compared as dates, never converted to UTC.
        if (req.ReceivedDate is null)
        {
            errors.Add("receivedDate", "Data e hyrjes është e detyrueshme.");
        }
        else if (req.ReceivedDate > todayInTirane)
        {
            errors.Add("receivedDate", "Data e hyrjes nuk mund të jetë në të ardhmen.");
        }

        if (req.ActDate is { } actDate && req.ReceivedDate is { } received && actDate > received)
        {
            errors.Add("actDate", "Data e aktit nuk mund të jetë pas datës së hyrjes.");
        }

        if (withParties)
        {
            ValidateParties(req.Parties, errors);
        }

        if (req.FirstHearing is null)
        {
            errors.Add("firstHearing", "Seanca e parë është e detyrueshme.");
            return null;
        }

        if (req.FirstHearing.OtherTypeText is { Length: > MaxOtherTypeLength })
        {
            errors.Add("firstHearing.otherTypeText", $"Përshkrimi i llojit mund të ketë deri në {MaxOtherTypeLength} karaktere.");
        }

        var hearingUtc = ParseHearingAt(req.FirstHearing.At, "firstHearing.at", errors);
        if (hearingUtc is { } utc && req.ActDate is { } act && TiraneTime.DateOf(utc) < act)
        {
            errors.Add("firstHearing.at", "Seanca nuk mund të jetë para datës së aktit.");
        }

        return hearingUtc;
    }

    /// <summary>
    /// A hearing date and time: required, ISO 8601 WITH an offset or Z (a bare local time would be ambiguous),
    /// returned in UTC. Null (with the error added under <paramref name="field"/>) when missing or invalid.
    /// </summary>
    public static DateTime? ParseHearingAt(string? value, string field, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Data dhe ora e seancës janë të detyrueshme.");
            return null;
        }

        var at = value.Trim();
        if (!OffsetRegex().IsMatch(at)
            || !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            errors.Add(field, "Data dhe ora e seancës nuk janë të vlefshme (ISO 8601 me zonë kohore, p.sh. 2026-11-05T09:30:00+01:00).");
            return null;
        }

        return parsed.UtcDateTime;
    }

    /// <summary>Each party's format, plus duplicates inside the request; roles and institutions are checked by the handler.</summary>
    private static void ValidateParties(IReadOnlyList<PartyRequest>? parties, FieldErrors errors)
    {
        // TODO(client-q-min-parties): at least one party required at registration (default).
        if (parties is null || parties.Count == 0)
        {
            errors.Add("parties", "Shtoni të paktën një palë.");
            return;
        }

        var seenInstitutions = new HashSet<Guid>();
        var seenNames = new HashSet<(string Role, string Name)>();
        for (var i = 0; i < parties.Count; i++)
        {
            var prefix = $"parties[{i}].";
            var kind = PartyValidator.ValidateFormat(parties[i], prefix, errors);
            var p = parties[i];
            if (kind is null || p is null)
            {
                continue;
            }

            if (kind == CaseParty.Institution)
            {
                if (p.InstitutionPublicId is { } institutionId && !seenInstitutions.Add(institutionId))
                {
                    errors.Add($"{prefix}institutionPublicId", PartyValidator.DuplicateInstitutionMessage);
                }
            }
            else if (!errors.Has($"{prefix}displayName") && !string.IsNullOrWhiteSpace(p.PartyRoleCode)
                     && !seenNames.Add((p.PartyRoleCode.Trim(), PartyValidator.NormalizeName(p.DisplayName!))))
            {
                errors.Add($"{prefix}displayName", PartyValidator.DuplicateNameMessage);
            }
        }
    }

    private static void Required(FieldErrors errors, string field, string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
        {
            errors.Add(field, $"{label} është i detyrueshëm (deri në {max} karaktere).");
        }
    }

    // the value must state its offset: "Z" or "+01:00"
    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetRegex();
}
