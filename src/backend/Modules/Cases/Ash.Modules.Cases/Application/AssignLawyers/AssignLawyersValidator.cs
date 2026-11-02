using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.AssignLawyers;

public static class AssignLawyersValidator
{
    public const string InvalidMessage = "Të dhënat e caktimit nuk janë të vlefshme.";

    /// <summary>The request alone, before any database read (primary ≠ secondary is checked here, before the unique key).</summary>
    public static Error? Validate(AssignLawyersRequest req)
    {
        var errors = new FieldErrors();
        if (req.PrimaryUserPublicId is null || req.PrimaryUserPublicId == Guid.Empty)
        {
            errors.Add("primaryUserPublicId", "Zgjidhni avokatin primar.");
        }

        if (req.SecondaryUserPublicId == Guid.Empty)
        {
            errors.Add("secondaryUserPublicId", "Avokati sekondar nuk është i vlefshëm.");
        }
        else if (req.SecondaryUserPublicId is not null && req.SecondaryUserPublicId == req.PrimaryUserPublicId)
        {
            errors.Add("secondaryUserPublicId", "Avokati sekondar duhet të jetë i ndryshëm nga avokati primar.");
        }

        if (req.Priority is not null && !Case.Priorities.Contains(req.Priority))
        {
            errors.Add("priority", "Prioriteti duhet të jetë NONE, LOW, MEDIUM ose HIGH.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError(InvalidMessage) : null;
    }
}
