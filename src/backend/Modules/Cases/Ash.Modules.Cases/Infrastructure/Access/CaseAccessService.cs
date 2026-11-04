using Ash.Modules.Cases.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Infrastructure.Access;

/// <summary>Scoped: one query per (user, case) per request, then answered from memory.</summary>
public sealed class CaseAccessService(CasesDbContext db, ICurrentUser user, IReferenceData reference) : ICaseAccessService
{
    private readonly Dictionary<(long UserId, long CaseId), Task<CaseAccess>> _cache = [];

    public IQueryable<Case> ApplyScope(IQueryable<Case> cases) => CaseScopeQuery.Apply(cases, db, user);

    public async Task<bool> CanViewAsync(long caseId, CancellationToken cancellationToken = default) =>
        (await GetAsync(caseId, cancellationToken)).CanView;

    public async Task<bool> HasAsync(long caseId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var access = await GetAsync(caseId, cancellationToken);
        return access.CanView && access.Permissions.Contains(permissionCode);
    }

    public async Task RequireAsync(long caseId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var access = await GetAsync(caseId, cancellationToken);
        if (!access.CanView)
        {
            throw new CaseAccessException(Error.NotFound("case.not_found", "Çështja nuk u gjet."));
        }

        if (!access.Permissions.Contains(permissionCode))
        {
            throw new CaseAccessException(Error.Forbidden("case.forbidden", "Nuk keni leje për këtë veprim."));
        }
    }

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(long caseId, CancellationToken cancellationToken = default) =>
        (await GetAsync(caseId, cancellationToken)).Permissions;

    public async Task<bool> IsDossierArchivedAsync(long caseId, CancellationToken cancellationToken = default) =>
        (await GetAsync(caseId, cancellationToken)).Archived;

    /// <inheritdoc/>
    public async Task<bool> CanViewDossierDocumentsAsync(long dossierId, CancellationToken cancellationToken = default)
    {
        var id = (ulong)dossierId;
        var levels = await db.Cases.AsNoTracking().Where(c => c.DossierId == id).Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var level in levels)
        {
            if (await CanViewAsync((long)level, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public const string LinkPreviousPermission = "case.link_previous";
    public const string ViewPermission = "case.view";
    public const string EditRegistrationPermission = "case.edit_registration";

    /// <summary>The institution user assigns its jurist — only where the user's institution has an active link.</summary>
    public const string AssignJuristPermission = "institution.assign_jurist";

    /// <summary>
    /// The registering office's Protokoll corrects the registration while the case waits for a lawyer: parties,
    /// and the judge and the object (typos, task 028).
    /// </summary>
    private static readonly string[] RegistrationDeskActions =
        [EditRegistrationPermission, "party.add", "party.edit", "party.delete", "case.change_judge", "case.change_object"];

    public const string DecisionRecordPermission = "decision.record";
    public const string DocumentUploadPermission = "document.upload";
    public const string CloseFinalPermission = "decision.close_final";

    /// <summary>
    /// What still applies to a CLOSED case, if the user holds it (the next level continues the work):
    /// - view, and the case chat (lawyers discuss the appeal after closing);
    /// - decision.record: complete the decision later (task 037 — the reasoned decision arrives, the appeal is filed,
    ///   it becomes final);
    /// - document.upload: ONLY the decision document (type VENDIM) — the upload handler refuses any other type on a
    ///   CLOSED case with 409 "Në një çështje të mbyllur ngarkohet vetëm vendimi.";
    /// - decision.close_final: archive the dossier (task 038) — further narrowed by ApplyStateRules to the latest level
    ///   with a final decision.
    /// Every other write action is off for a CLOSED case.
    /// </summary>
    public static readonly IReadOnlySet<string> ActionsAllowedWhenClosed = new HashSet<string>
    {
        ViewPermission,
        "chat.case",
        DecisionRecordPermission,
        DocumentUploadPermission,
        CloseFinalPermission,
    };

    /// <summary>The Kryetar of the case's office (system permissions held for their own office only).</summary>
    private static readonly string[] OfficeHeadActions = ["case.assign_lawyer", "case.transfer", "case.set_priority_any"];

    public RegistrationLookupScope GetRegistrationLookupScope() =>
        // All offices: confirmed by the client (Q1a) — a next-level summons may arrive at another office.
        user.HasPermission(LinkPreviousPermission) ? RegistrationLookupScope.AllOffices : RegistrationLookupScope.None;

    private Task<CaseAccess> GetAsync(long caseId, CancellationToken cancellationToken)
    {
        var key = (user.UserId, caseId);
        if (!_cache.TryGetValue(key, out var task))
        {
            task = LoadAsync(caseId, cancellationToken);
            _cache[key] = task;
        }

        return task;
    }

    private async Task<CaseAccess> LoadAsync(long caseId, CancellationToken cancellationToken)
    {
        var id = (ulong)caseId;
        var userId = (uint)user.UserId;

        var rows = await (
            from c in db.Cases.Where(c => c.Id == id)
            join p in db.CasePermissions.Where(p => p.UserId == userId) on c.Id equals p.CaseId into ps
            from p in ps.DefaultIfEmpty()
            select new
            {
                c.OfficeId,
                c.LifecycleStatus,
                c.RegisteredBy,
                c.CategoryId,
                Archived = db.Dossiers.Any(d => d.Id == c.DossierId && d.ArchivedAt != null),
                // archiving (task 038) is possible on the dossier's LATEST level once its decision is final
                ArchivableLevel = db.CaseDecisions.Any(d => d.CaseId == c.Id && d.IsFinal)
                    && !db.Cases.Any(n => n.DossierId == c.DossierId && n.LevelSeq > c.LevelSeq),
                HasProfile = db.CaseAccess.Any(a => a.UserId == userId && a.CaseId == c.Id),
                Permission = p == null ? null : p.PermissionCode
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return CaseAccess.None;
        }

        // Same rule as CaseScopeQuery.Apply, evaluated on the one loaded row.
        var first = rows[0];
        var canView = first.HasProfile
            || user.HasPermission(CaseScopeQuery.ViewAllPermission)
            || (user.HasPermission(CaseScopeQuery.ViewOfficePermission) && user.OfficeId == first.OfficeId)
            || (first.RegisteredBy == userId && first.LifecycleStatus == CaseScopeQuery.RegisteredStatus);

        if (!canView)
        {
            return CaseAccess.None;
        }

        // an archived dossier is read-only on EVERY level: view only (ActionsAllowedWhenClosed does not apply)
        if (first.Archived)
        {
            return new CaseAccess(true, ArchivedPermissions, Archived: true);
        }

        // institution.assign_jurist acts only on cases its user's institution is linked to (task 027)
        var institutionLinked = user.InstitutionId is { } institutionId && user.HasPermission(AssignJuristPermission)
            && await db.CaseInstitutions.AnyAsync(
                l => l.CaseId == id && l.EndedAt == null && l.InstitutionId == (uint)institutionId, cancellationToken);

        // a DELEGIM / RELATIM category belongs to the delegation (task 027): is_delegation from the lookup table
        var delegatedCategory = first.CategoryId is { } categoryId
            && (await reference.ListCaseCategoriesAsync(cancellationToken)).Any(x => x.Id == categoryId && x.IsDelegation);

        var effective = Effective(rows.Select(r => r.Permission), first.OfficeId, first.LifecycleStatus, institutionLinked);
        ApplyStateRules(effective, first.LifecycleStatus, delegatedCategory, first.ArchivableLevel);
        return new CaseAccess(true, effective);
    }

    public const string AssignLawyerPermission = "case.assign_lawyer";
    public const string TransferPermission = "case.transfer";
    public const string DelegatePermission = "case.delegate";
    public const string SetCategoryPermission = "case.set_category";

    // Every state-dependent action belongs here, never in the UI or only in a handler.
    /// <summary>
    /// Actions that exist only in one state of the case, whoever holds them (task 029): the UI shows a button exactly
    /// when its code is in the effective permissions. The handlers still check the state first, so the API answers
    /// with its specific 409 (already assigned, not assigned, not active, delegated) rather than a 403.
    /// - case.assign_lawyer: only while REGISTERED (afterwards it is a transfer);
    /// - case.transfer, case.delegate: only while ACTIVE (there is a lawyer and the case is open);
    /// - case.set_category: not while the category is DELEGIM / RELATIM (it changes through the delegation);
    /// - decision.close_final (archive the dossier, task 038): only on the dossier's latest level, CLOSED with a final
    ///   decision.
    /// CLOSED is handled by <see cref="ActionsAllowedWhenClosed"/>; an archived dossier by <see cref="ArchivedPermissions"/>.
    /// </summary>
    private static void ApplyStateRules(HashSet<string> effective, string status, bool delegatedCategory, bool archivableLevel)
    {
        if (status != Case.Closed || !archivableLevel)
        {
            effective.Remove(CloseFinalPermission);
        }

        if (status != Case.Registered)
        {
            effective.Remove(AssignLawyerPermission);
        }

        if (status != Case.Active)
        {
            effective.Remove(TransferPermission);
            effective.Remove(DelegatePermission);
        }

        if (delegatedCategory)
        {
            effective.Remove(SetCategoryPermission);
        }
    }

    /// <summary>
    /// Case permissions ∪ system permissions that apply to this case in its current state (office head actions in
    /// the own office; institution.assign_jurist where the user's institution is linked). A CLOSED case keeps only
    /// what is in <see cref="ActionsAllowedWhenClosed"/>.
    /// </summary>
    private HashSet<string> Effective(IEnumerable<string?> casePermissions, ushort officeId, string status, bool institutionLinked)
    {
        var effective = casePermissions.Where(p => p is not null).Select(p => p!).ToHashSet();
        effective.Add(ViewPermission); // viewing through the office or view_all is still viewing
        if (institutionLinked)
        {
            effective.Add(AssignJuristPermission);
        }

        if (status == Case.Closed)
        {
            effective.IntersectWith(ActionsAllowedWhenClosed);
            return effective;
        }

        var ownOffice = user.OfficeId == officeId;
        if (ownOffice && status == Case.Registered && user.HasPermission(EditRegistrationPermission))
        {
            effective.UnionWith(RegistrationDeskActions);
        }

        if (ownOffice)
        {
            effective.UnionWith(OfficeHeadActions.Where(user.HasPermission));
        }

        return effective;
    }

    /// <summary>
    /// An archived dossier (task 038): only viewing, on every level — no chat, no decision, no upload. Its documents stay
    /// downloadable (they need case.view on any level, task 034). TODO(client-q-unarchive): undoing an archiving is not
    /// possible now; later admin-only, with a reason and an audit row.
    /// </summary>
    public static readonly IReadOnlySet<string> ArchivedPermissions = new HashSet<string> { ViewPermission };

    private sealed record CaseAccess(bool CanView, IReadOnlySet<string> Permissions, bool Archived = false)
    {
        public static CaseAccess None { get; } = new(false, new HashSet<string>());
    }
}
