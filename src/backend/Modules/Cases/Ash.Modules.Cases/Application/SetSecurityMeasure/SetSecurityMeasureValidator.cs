using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.SetSecurityMeasure;

public static class SetSecurityMeasureValidator
{
    public const int MaxDecisionNumberLength = 50;

    /// <summary>TEXT column: 65 535 bytes, so at most ~16 000 four-byte characters; 10 000 leaves room.</summary>
    public const int MaxDispositiveLength = 10_000;

    /// <summary>Only "active" and rowVersion when lifting; the decision fields are ignored then.</summary>
    public static Error? Validate(SetSecurityMeasureRequest req)
    {
        var errors = new FieldErrors();
        if (req.Active is null)
        {
            errors.Add("active", "Zgjidhni nëse masa e sigurimit vendoset apo hiqet.");
        }
        else if (req.Active.Value)
        {
            if (string.IsNullOrWhiteSpace(req.DecisionNumber))
            {
                errors.Add("decisionNumber", "Numri i vendimit është i detyrueshëm.");
            }
            else if (req.DecisionNumber.Trim().Length > MaxDecisionNumberLength)
            {
                errors.Add("decisionNumber", $"Numri i vendimit mund të ketë deri në {MaxDecisionNumberLength} karaktere.");
            }

            if (req.DecisionDate is null)
            {
                errors.Add("decisionDate", "Data e vendimit është e detyrueshme.");
            }

            if (req.Dispositive is not null && req.Dispositive.Trim().Length > MaxDispositiveLength)
            {
                errors.Add("dispositive", $"Dispozitivi mund të ketë deri në {MaxDispositiveLength} karaktere.");
            }
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError("Të dhënat e masës së sigurimit nuk janë të vlefshme.") : null;
    }
}
