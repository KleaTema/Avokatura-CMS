using Ash.Modules.Cases.Application.ArchiveDossier;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Documents.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Infrastructure.Access;

/// <summary>Scoped: one query per (user, case) per request, then answered from memory.</summary>
public sealed class CaseAccessService(CasesDbContext db, ICurrentUser user, IReferenceData reference, IDocumentService documents) : ICaseAccessService
{
    private readonly Dictionary<(long UserId, long CaseId), Task<CaseAccess>> _cache = [];

    public IQueryable<Case> ApplyScope(IQueryable<Case> cases) => CaseScopeQuery.Apply(cases, db, user);

    /// <summary>The scope plus the earlier levels of its dossiers (D2, task 053f) — what <see cref="CanViewAsync"/> opens.</summary>
    public IQueryable<Case> ApplyScopeWithEarlierLevels(IQueryable<Case> cases) => CaseScopeQuery.ApplyWithEarlierLevels(cases, db, user);

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

    /// <summary>
    /// Client decision D1 (12.11.2026): the Kryetar records and completes the decision and archives on the cases of
    /// their office — the system permission decision.record_office (script 020) grants decision.record and
    /// decision.close_final there, also on a CLOSED level (completing the decision later, archiving) — and on the
    /// cases they supervise (task 053d). Never decided by the role name.
    /// </summary>
    public const string RecordDecisionOfficePermission = "decision.record_office";

    private static readonly string[] OfficeDecisionActions = [DecisionRecordPermission, CloseFinalPermission];

    public RegistrationLookupScope GetRegistrationLookupScope() =>
        // All offices of the country: confirmed by the client (Q1a; task 053j b: the Protokoll is a CENTRAL role). A
        // lookup of registration-level data only — never case.view (the full case page, documents, team, chat, history).
        // TODO(client-q-protokoll-full-view): whether the Protokoll should also open the full case page.
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
                c.DossierId,
                c.LevelSeq,
                c.OfficeId,
                c.LifecycleStatus,
                c.RegisteredBy,
                c.CategoryId,
                Archived = db.Dossiers.Any(d => d.Id == c.DossierId && d.ArchivedAt != null),
                HasProfile = db.CaseAccess.Any(a => a.UserId == userId && a.CaseId == c.Id),
                Permission = p == null ? null : p.PermissionCode
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return CaseAccess.None;
        }

        // Same rule as CaseScopeQuery.Apply, evaluated on the one loaded row.
        var first = rows[0];
        // task 053d: under the Kryetar's supervision — they act as on a case of their own office
        var supervised = user.HasPermission(CaseScopeQuery.ViewSupervisedPermission)
            && await db.Cases.Where(c => c.Id == id).AnyAsync(CaseScopeQuery.Supervised(db, user.UserId), cancellationToken);
        var canView = first.HasProfile
            || user.HasPermission(CaseScopeQuery.ViewAllPermission)
            || (user.HasPermission(CaseScopeQuery.ViewOfficePermission) && user.OfficeId == first.OfficeId)
            || supervised
            || (first.RegisteredBy == userId && first.LifecycleStatus == CaseScopeQuery.RegisteredStatus);

        if (!canView)
        {
            // client decision D2 (task 053f): whoever can view a LATER level of the dossier views this one completely,
            // read-only (case.view only). Never backwards: X/1 gives nothing on X/2. Own access (above) wins: the X/1
            // lawyer keeps X/1's rights even when also on X/2 (decision of 13.11.2026).
            return await ViewableLaterLevelAsync(first.DossierId, first.LevelSeq, cancellationToken)
                ? new CaseAccess(true, ViewOnlyPermissions, ReadOnlyEarlierLevel: true)
                : CaseAccess.None;
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

        var effective = Effective(rows.Select(r => r.Permission), first.OfficeId, first.LifecycleStatus, institutionLinked, supervised,
            first.RegisteredBy == userId);
        // archiving (task 038, client decision A): ArchiveRule — asked only of a user who would hold it
        var archivable = effective.Contains(CloseFinalPermission) && first.LifecycleStatus == Case.Closed
            && await ArchiveRule.IsArchivableAsync(db, documents, await db.Cases.AsNoTracking().SingleAsync(c => c.Id == id, cancellationToken), cancellationToken);
        ApplyStateRules(effective, first.LifecycleStatus, delegatedCategory, archivable);
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
        else
        {
            // no lawyer yet: there is no decision to record (the handler answers 409 case.not_active) — e.g. the Kryetar
            effective.Remove(DecisionRecordPermission);
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
    private HashSet<string> Effective(
        IEnumerable<string?> casePermissions, ushort officeId, string status, bool institutionLinked, bool supervised,
        bool registeredByUser)
    {
        var effective = casePermissions.Where(p => p is not null).Select(p => p!).ToHashSet();
        effective.Add(ViewPermission); // viewing through the office or view_all is still viewing
        if (institutionLinked)
        {
            effective.Add(AssignJuristPermission);
        }

        // the Kryetar's actions apply on the own office's cases AND on the supervised ones (task 053d); the desk's
        // registration corrections stay with the own office (registeredOffice below)
        var registeredOffice = user.OfficeId == officeId;
        var ownOffice = registeredOffice || supervised;
        if (ownOffice && user.HasPermission(RecordDecisionOfficePermission))
        {
            effective.UnionWith(OfficeDecisionActions);
        }

        if (status == Case.Closed)
        {
            effective.IntersectWith(ActionsAllowedWhenClosed);
            return effective;
        }

        // the desk's corrections: its own office's registrations, and (task 053j) the ones it sent to another office
        if ((registeredOffice || registeredByUser) && status == Case.Registered && user.HasPermission(EditRegistrationPermission))
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

    /// <summary>An earlier level seen through a later one (D2, task 053f): viewing only — no chat, upload or decision.</summary>
    public static readonly IReadOnlySet<string> ViewOnlyPermissions = new HashSet<string> { ViewPermission };

    /// <summary>
    /// Whether the user can view a later level of the dossier (a level they can view with their own access, or itself
    /// through a still later one — so X/3 opens X/2 and X/1). Answered from the per-request cache.
    /// </summary>
    private async Task<bool> ViewableLaterLevelAsync(ulong dossierId, ushort levelSeq, CancellationToken ct)
    {
        var later = await db.Cases.AsNoTracking()
            .Where(c => c.DossierId == dossierId && c.LevelSeq > levelSeq)
            .OrderBy(c => c.LevelSeq)
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (var id in later)
        {
            if ((await GetAsync((long)id, ct)).CanView)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> IsReadOnlyEarlierLevelAsync(long caseId, CancellationToken cancellationToken = default) =>
        (await GetAsync(caseId, cancellationToken)).ReadOnlyEarlierLevel;

    private sealed record CaseAccess(bool CanView, IReadOnlySet<string> Permissions, bool Archived = false, bool ReadOnlyEarlierLevel = false)
    {
        public static CaseAccess None { get; } = new(false, new HashSet<string>());
    }
}
