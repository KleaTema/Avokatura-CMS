using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.TransferLawyer;

public static class TransferLawyerValidator
{
    public const string InvalidMessage = "Të dhënat e transferimit nuk janë të vlefshme.";
    public const int MaxReasonLength = 500;

    public const string Keep = "keep";
    public const string Set = "set";
    public const string Remove = "remove";

    /// <summary>The request alone, before any database read. Returns the secondary action (keep when not sent).</summary>
    public static Error? Validate(TransferLawyerRequest req, out string secondaryAction)
    {
        var errors = new FieldErrors();
        secondaryAction = Keep;

        if (req.NewPrimaryUserPublicId == Guid.Empty)
        {
            errors.Add("newPrimaryUserPublicId", "Avokati i ri primar nuk është i vlefshëm.");
        }

        if (req.Secondary is { } s)
        {
            if (s.Action is null || s.Action is not (Keep or Set or Remove))
            {
                errors.Add("secondary.action", "Veprimi për avokatin sekondar duhet të jetë keep, set ose remove.");
            }
            else
            {
                secondaryAction = s.Action;
            }

            if (s.Action == Set && (s.UserPublicId is null || s.UserPublicId == Guid.Empty))
            {
                errors.Add("secondary.userPublicId", "Zgjidhni avokatin sekondar.");
            }
            else if (s.Action is Keep or Remove && s.UserPublicId is not null)
            {
                errors.Add("secondary.userPublicId", "Avokati sekondar zgjidhet vetëm për veprimin set.");
            }
            else if (s.Action == Set && s.UserPublicId == req.NewPrimaryUserPublicId)
            {
                errors.Add("secondary.userPublicId", "Avokati sekondar duhet të jetë i ndryshëm nga avokati primar.");
            }
        }

        if (req.Reason is not null && req.Reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", $"Arsyeja mund të ketë deri në {MaxReasonLength} karaktere.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        if (!errors.Any && req.NewPrimaryUserPublicId is null && secondaryAction == Keep)
        {
            return NoChange();
        }

        return errors.Any ? errors.ToError(InvalidMessage) : null;
    }

    public static Error NoChange() =>
        Error.Validation("transfer.no_change", "Zgjidhni avokatin e ri primar ose ndryshoni avokatin sekondar.");
}
