using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.ChangeJudge;

public static class ChangeJudgeValidator
{
    public const string InvalidMessage = "Gjyqtari nuk është i vlefshëm.";

    public static Error? Validate(ChangeJudgeRequest req)
    {
        var errors = new FieldErrors();
        if (req.JudgeId is null or <= 0 or > uint.MaxValue)
        {
            errors.Add("judgeId", "Zgjidhni gjyqtarin.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError(InvalidMessage) : null;
    }
}
