using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Contracts;

/// <summary>The one answer of every write endpoint on an archived dossier (task 038), in every module.</summary>
public static class DossierErrors
{
    public static Error Archived { get; } = Error.Conflict("dossier.archived", "Dosja është e arkivuar.");
}
