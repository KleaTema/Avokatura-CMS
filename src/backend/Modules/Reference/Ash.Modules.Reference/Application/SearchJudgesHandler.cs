using System.Globalization;
using System.Text;
using Ash.Modules.Reference.Contracts;

namespace Ash.Modules.Reference.Application;

public sealed class SearchJudgesHandler(IReferenceData reference)
{
    public const int MaxResults = 50;

    public async Task<IReadOnlyList<JudgeResponse>> Handle(int? courtId, string? q, CancellationToken ct)
    {
        var judges = (await reference.ListJudgesAsync(ct)).Where(x => x.IsActive);
        if (courtId is not null)
        {
            judges = judges.Where(x => x.CourtId == courtId);
        }

        var term = Fold(q);
        if (term.Length > 0)
        {
            judges = judges.Where(x => Fold(x.FirstName).Contains(term, StringComparison.Ordinal)
                                       || Fold(x.LastName).Contains(term, StringComparison.Ordinal));
        }

        return judges.Take(MaxResults)
            .Select(x => new JudgeResponse(x.Id, x.FirstName, x.LastName, x.CourtId))
            .ToList();
    }

    /// <summary>Lower-cases and strips diacritics so "cela" matches "Çela" and "deshira" matches "Dëshira".</summary>
    internal static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var c in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
