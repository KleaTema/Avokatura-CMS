using System.Text.RegularExpressions;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.UpdateCaseData;

public sealed record ClaimValues(decimal? Amount, string? Currency, decimal? AreaM2);

public static partial class UpdateClaimValidator
{
    public const string DefaultCurrency = "ALL";

    /// <summary>
    /// The request checked and parsed. Empty text counts as null. A currency without an amount is ignored (there is
    /// nothing to measure); with an amount it must be 3 capital letters, "ALL" when omitted.
    /// </summary>
    public static Result<ClaimValues> Validate(UpdateClaimRequest req)
    {
        var errors = new FieldErrors();
        decimal? amount = null;
        decimal? area = null;
        string? currency = null;

        if (!string.IsNullOrWhiteSpace(req.ClaimAmount))
        {
            if (DecimalText.TryParse(req.ClaimAmount.Trim(), DecimalText.MoneyIntegerDigits, out var a))
            {
                amount = a;
            }
            else
            {
                errors.Add("claimAmount",
                    "Shuma duhet të jetë një numër jo negativ me deri në 2 shifra pas pikës (p.sh. 1250000.50) dhe deri në 16 shifra para saj.");
            }

            currency = string.IsNullOrWhiteSpace(req.ClaimCurrency) ? DefaultCurrency : req.ClaimCurrency.Trim();
            if (!CurrencyCode().IsMatch(currency))
            {
                errors.Add("claimCurrency", "Monedha duhet të jetë kodi ISO me 3 shkronja, p.sh. ALL ose EUR.");
            }
        }

        if (!string.IsNullOrWhiteSpace(req.ClaimAreaM2))
        {
            if (DecimalText.TryParse(req.ClaimAreaM2.Trim(), DecimalText.AreaIntegerDigits, out var m2))
            {
                area = m2;
            }
            else
            {
                errors.Add("claimAreaM2",
                    "Sipërfaqja duhet të jetë një numër jo negativ me deri në 2 shifra pas pikës dhe deri në 12 shifra para saj.");
            }
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError("Vlera e padisë nuk është e vlefshme.") : new ClaimValues(amount, currency, area);
    }

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCode();
}
