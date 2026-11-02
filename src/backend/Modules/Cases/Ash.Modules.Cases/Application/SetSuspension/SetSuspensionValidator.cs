using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.SetSuspension;

public static class SetSuspensionValidator
{
    public const int MaxReasonLength = 500;

    public static Error? Validate(SetSuspensionRequest req)
    {
        var errors = new FieldErrors();
        if (req.Suspended is null)
        {
            errors.Add("suspended", "Zgjidhni nëse çështja pezullohet apo rifillon.");
        }

        if (req.Reason is not null && req.Reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", $"Arsyeja mund të ketë deri në {MaxReasonLength} karaktere.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError("Të dhënat e pezullimit nuk janë të vlefshme.") : null;
    }
}
