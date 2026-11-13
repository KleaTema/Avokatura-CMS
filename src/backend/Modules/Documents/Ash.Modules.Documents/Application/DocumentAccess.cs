using Ash.Modules.Cases.Contracts;

namespace Ash.Modules.Documents.Application;

/// <summary>
/// Who may see a document (tasks 034, 053f), the ONE place in this module: a document of a level follows that level —
/// visible exactly when the user can view the level (ICaseAccessService, client decision D2: a later level opens every
/// earlier one, never backwards). A dossier-wide document (no level) is visible to whoever can view any level.
/// </summary>
internal static class DocumentAccess
{
    public static async Task<bool> CanViewAsync(ICaseAccessService access, ulong dossierId, ulong? caseId, CancellationToken ct) =>
        caseId is { } level
            ? await access.CanViewAsync((long)level, ct)
            : await access.CanViewDossierDocumentsAsync((long)dossierId, ct);
}
