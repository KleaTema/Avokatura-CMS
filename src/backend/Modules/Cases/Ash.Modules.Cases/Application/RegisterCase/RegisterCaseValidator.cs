using System.Globalization;
using System.Text.RegularExpressions;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Cases.Application.RegisterCase;

/// <summary>
/// Checks that need no database: required fields, lengths and dates. Keys are the camelCase request paths
/// ("firstHearing.at") so the UI can show each message next to its field.
/// </summary>
public static partial class RegisterCaseValidator
{
    public const int MaxNumberLength = 50;
    public const int MaxObjectLength = 4000;
    public const int MaxOtherTypeLength = 200;

    public const string JudgeMissingHint = "Nëse gjyqtari mungon në listë, kërkoni administratorit ta shtojë.";

    public sealed class FieldErrors
    {
        private readonly Dictionary<string, List<string>> _errors = [];

        public bool Any => _errors.Count > 0;

        public bool Has(string field) => _errors.ContainsKey(field);

        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var list))
            {
                _errors[field] = list = [];
            }

            list.Add(message);
        }

        public IReadOnlyDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(x => x.Key, x => x.Value.ToArray());
    }

    /// <summary>Validates the request; returns the hearing time in UTC when it could be read.</summary>
    public static DateTime? Validate(RegisterCaseRequest req, DateOnly todayInTirane, FieldErrors errors)
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

        if (req.FirstHearing is null)
        {
            errors.Add("firstHearing", "Seanca e parë është e detyrueshme.");
            return null;
        }

        if (req.FirstHearing.OtherTypeText is { Length: > MaxOtherTypeLength })
        {
            errors.Add("firstHearing.otherTypeText", $"Përshkrimi i llojit mund të ketë deri në {MaxOtherTypeLength} karaktere.");
        }

        if (string.IsNullOrWhiteSpace(req.FirstHearing.At))
        {
            errors.Add("firstHearing.at", "Data dhe ora e seancës së parë janë të detyrueshme.");
            return null;
        }

        var at = req.FirstHearing.At.Trim();
        if (!OffsetRegex().IsMatch(at)
            || !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            errors.Add("firstHearing.at", "Data dhe ora e seancës nuk janë të vlefshme (ISO 8601 me zonë kohore, p.sh. 2026-11-05T09:30:00+01:00).");
            return null;
        }

        var hearingUtc = parsed.UtcDateTime;
        if (req.ActDate is { } act && TiraneTime.DateOf(hearingUtc) < act)
        {
            errors.Add("firstHearing.at", "Seanca nuk mund të jetë para datës së aktit.");
        }

        return hearingUtc;
    }

    private static void Required(FieldErrors errors, string field, string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
        {
            errors.Add(field, $"{label} është i detyrueshëm (deri në {max} karaktere).");
        }
    }

    // the value must state its offset: "Z" or "+01:00"; a bare local time would be ambiguous
    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetRegex();
}
