using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.SetCategory;

public static class SetCategoryValidator
{
    public const string InvalidMessage = "Kategoria nuk është e vlefshme.";

    public static Error? Validate(SetCategoryRequest req)
    {
        var errors = new FieldErrors();
        if (string.IsNullOrWhiteSpace(req.CategoryCode))
        {
            errors.Add("categoryCode", "Zgjidhni kategorinë.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError(InvalidMessage) : null;
    }
}
