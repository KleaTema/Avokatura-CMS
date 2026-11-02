using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.DelegateCase;

public static class DelegateCaseValidator
{
    public const string InvalidMessage = "Të dhënat e delegimit nuk janë të vlefshme.";
    public const int MaxReasonLength = 500;

    /// <summary>The mode's code is checked against the categories table (is_delegation) in the handler.</summary>
    public static Error? ValidateDelegate(DelegateCaseRequest req)
    {
        var errors = new FieldErrors();
        if (req.InstitutionPublicId is null || req.InstitutionPublicId == Guid.Empty)
        {
            errors.Add("institutionPublicId", "Zgjidhni institucionin.");
        }

        if (string.IsNullOrWhiteSpace(req.Mode))
        {
            errors.Add("mode", "Zgjidhni delegim ose relatim.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError(InvalidMessage) : null;
    }

    public static Error? ValidateUndelegate(UndelegateCaseRequest req)
    {
        var errors = new FieldErrors();
        if (req.Reason is not null && req.Reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", $"Arsyeja mund të ketë deri në {MaxReasonLength} karaktere.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError(InvalidMessage) : null;
    }
}
