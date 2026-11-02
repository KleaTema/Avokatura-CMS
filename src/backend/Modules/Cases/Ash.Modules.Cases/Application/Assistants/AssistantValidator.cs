using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.Assistants;

public static class AssistantValidator
{
    public const string InvalidMessage = "Të dhënat e ndihmësit nuk janë të vlefshme.";

    public static Error? ValidateAdd(AddAssistantRequest req)
    {
        var errors = new FieldErrors();
        if (req.UserPublicId is null || req.UserPublicId == Guid.Empty)
        {
            errors.Add("userPublicId", "Zgjidhni ndihmësin ligjor.");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i çështjes. Rifreskoni faqen.");
        }

        return errors.Any ? errors.ToError(InvalidMessage) : null;
    }

    public static Error? ValidateRemove(uint? rowVersion) =>
        rowVersion is null
            ? Error.ValidationFields(InvalidMessage, new Dictionary<string, string[]>
            {
                ["rowVersion"] = ["Mungon versioni i çështjes. Rifreskoni faqen."]
            })
            : null;
}
