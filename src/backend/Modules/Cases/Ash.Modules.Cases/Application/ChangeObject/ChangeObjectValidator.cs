using Ash.Modules.Cases.Application.RegisterCase;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.ChangeObject;

public static class ChangeObjectValidator
{
    /// <summary>Validated before trimming (null → 400); the limit is the registration's.</summary>
    public static Error? Validate(ChangeObjectRequest req)
    {
        var errors = new FieldErrors();
        if (req.ObjectText is null || req.ObjectText.Trim().Length == 0)
        {
            errors.Add("objectText", "Objekti është i detyrueshëm.");
        }
        else if (req.ObjectText.Trim().Length > RegisterCaseValidator.MaxObjectLength)
        {
            errors.Add("objectText", $"Objekti mund të ketë deri në {RegisterCaseValidator.MaxObjectLength} karaktere.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError("Objekti nuk është i vlefshëm.") : null;
    }
}
