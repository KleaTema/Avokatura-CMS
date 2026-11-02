using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.SetPriority;

public static class SetPriorityValidator
{
    public static Error? Validate(SetPriorityRequest req)
    {
        var errors = new FieldErrors();
        if (req.Priority is null || !Case.Priorities.Contains(req.Priority))
        {
            errors.Add("priority", "Prioriteti duhet të jetë NONE, LOW, MEDIUM ose HIGH.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError("Prioriteti nuk është i vlefshëm.") : null;
    }
}
