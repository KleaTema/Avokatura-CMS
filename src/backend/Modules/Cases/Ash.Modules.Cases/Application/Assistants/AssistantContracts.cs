using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.Assistants;

/// <summary>POST /api/cases/{id}/assistants. Several assistants per case are allowed.</summary>
public sealed record AddAssistantRequest(Guid? UserPublicId, uint? RowVersion);

/// <summary>A legal assistant for the picker; <see cref="ActiveAssignments"/> = current assistant assignments on cases that are not CLOSED.</summary>
public sealed record AssignableAssistant(Guid PublicId, string FullName, int ActiveAssignments);

public sealed record AssistantRef(Guid PublicId, string Name);

/// <summary>The new row version for the next save, and the assistant that was added or removed.</summary>
public sealed record AssistantChangeResponse(uint RowVersion, AssistantRef Assistant);

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
