using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.DelegateCase;

/// <summary>Shared rules of delegating and undelegating (task 027).</summary>
internal static class Delegation
{
    public const string Permission = "case.delegate";

    public static Error NotActive() =>
        Error.Conflict("case.not_active", "Delegimi ndryshohet vetëm në një çështje aktive.");

    /// <summary>The state rule, checked before the permission (case.delegate exists only while ACTIVE).</summary>
    public static Task<Error?> OnlyActive(Case c) => Task.FromResult(c.LifecycleStatus == Case.Active ? null : NotActive());

    /// <summary>A parallel change of the links (1062 on uq_ci_active, or a deadlock) → 409, never 500.</summary>
    public static bool LinkSlotTaken(DbUpdateException ex) =>
        ex.InnerException is MySqlException m
        && ((m.Number == AssignmentInsertConflict.DuplicateKey && m.Message.Contains("uq_ci_active", StringComparison.Ordinal))
            || m.Number == AssignmentInsertConflict.Deadlock);
}

/// <summary>
/// POST delegate (case.delegate, the primary lawyer): the case goes to an institution that is already a party and
/// monitors it. One transaction: the MONITORING link is ENDED, a DELEGATED link inserted (end first: one active link
/// per case and institution), category = DELEGIM / RELATIM, subcategory = NULL. The institution's users and jurist then
/// get the INSTITUTION_DELEGATED profile (record the decision, change the judge, …).
/// Order: fields → 404 → CLOSED 409 → 403 → stale rowVersion 409 → not ACTIVE 409 → level without delegation 400 →
/// mode not a delegation category 400 → another delegation 409 → institution not monitoring 400.
/// TODO(client-q-relatim-rights): RELATIM gives the same rights as DELEGIM (default).
/// </summary>
internal sealed class DelegateCaseHandler(
    CasesDbContext db, CaseDataGate gate, IReferenceData reference, IInstitutionDirectory institutions,
    IHistoryWriter history, IAuditLog audit, IEventPublisher events, ICurrentUser user, IClock clock)
{
    public async Task<Result<DelegationResponse>> Handle(Guid casePublicId, DelegateCaseRequest req, CancellationToken ct)
    {
        var invalid = DelegateCaseValidator.ValidateDelegate(req);
        if (invalid is not null)
        {
            return invalid;
        }

        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, [Delegation.Permission], ct, Delegation.OnlyActive);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var level = await reference.GetCourtLevel(c.CourtLevelId, ct);
        if (level is not { AllowsDelegation: true })
        {
            return Error.Validation("case.delegation_not_allowed", "Delegimi lejohet vetëm në shkallën e parë dhe të dytë.");
        }

        var category = await reference.GetCaseCategory(req.Mode!.Trim(), ct);
        if (category is not { IsDelegation: true })
        {
            return Error.ValidationFields(DelegateCaseValidator.InvalidMessage, new Dictionary<string, string[]>
            {
                ["mode"] = ["Mënyra duhet të jetë DELEGIM ose RELATIM."]
            });
        }

        var links = await db.CaseInstitutions.Where(l => l.CaseId == c.Id && l.EndedAt == null).ToListAsync(ct);
        if (links.Any(l => l.LinkMode == CaseInstitution.Delegated))
        {
            return Error.Conflict("case.already_delegated", "Çështja është tashmë e deleguar te një institucion.");
        }

        var institution = (await institutions.GetByPublicIdsAsync([req.InstitutionPublicId!.Value], ct)).SingleOrDefault();
        var monitoring = institution is null
            ? null
            : links.SingleOrDefault(l => l.InstitutionId == (uint)institution.Id && l.LinkMode == CaseInstitution.Monitoring);
        if (monitoring is null)
        {
            return Error.ValidationFields(DelegateCaseValidator.InvalidMessage, new Dictionary<string, string[]>
            {
                ["institutionPublicId"] = ["Institucioni nuk është palë që monitoron këtë çështje; delegohet vetëm te një institucion i lidhur."]
            });
        }

        var currentCategory = (await reference.ListCaseCategoriesAsync(ct)).FirstOrDefault(x => x.Id == c.CategoryId);
        var currentSubcategory = (await reference.ListCaseSubcategoriesAsync(ct)).FirstOrDefault(x => x.Id == c.SubcategoryId);
        var now = clock.UtcNow;
        var me = (uint)user.UserId;
        try
        {
            // 1. end MONITORING, 2. start DELEGATED, 3. the category — one transaction
            monitoring.EndedAt = now;
            monitoring.EndedBy = me;
            await db.SaveChangesAsync(ct);
            db.CaseInstitutions.Add(new CaseInstitution
            {
                CaseId = c.Id,
                InstitutionId = monitoring.InstitutionId,
                LinkMode = CaseInstitution.Delegated,
                StartedAt = now,
                StartedBy = me
            });
            c.CategoryId = category.Id;
            c.SubcategoryId = null;
            var relates = category.Code != "DELEGIM";
            history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.Delegated,
                relates
                    ? $"Çështja iu relatua institucionit {institution!.Name}."
                    : $"Çështja iu delegua institucionit {institution!.Name}.",
                old: new { Category = currentCategory?.Name, Subcategory = currentSubcategory?.Name, Mode = CaseInstitution.Monitoring },
                @new: new { Institution = institution.Name, Category = category.Name, Subcategory = (string?)null, Mode = CaseInstitution.Delegated });
            if (await gate.SaveAsync(c, ct) is { } conflict)
            {
                return conflict;
            }
        }
        catch (DbUpdateException ex) when (Delegation.LinkSlotTaken(ex))
        {
            return AssignLawyersHandler.Changed();
        }

        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id,
            details: new { Action = "DELEGATE", InstitutionPublicId = institution.PublicId, Mode = category.Code });
        await events.PublishAsync(new CaseDelegated(c.PublicId, institution.PublicId, category.Code), ct);
        return new DelegationResponse(c.RowVersion, institution.PublicId, institution.Name, CaseInstitution.Delegated,
            new CodeName(category.Code, category.Name));
    }
}

/// <summary>
/// POST undelegate (case.delegate): the DELEGATED link is ENDED, a MONITORING link for the same institution inserted,
/// category = NULL (the lawyer sets Ndjekje again in the data tab). The institution's jurist keeps the assignment (its
/// profile becomes INSTITUTION_MONITORING through the link). UNDELEGATED with the reason, CaseUndelegated.
/// </summary>
internal sealed class UndelegateCaseHandler(
    CasesDbContext db, CaseDataGate gate, IReferenceData reference, IInstitutionDirectory institutions,
    IHistoryWriter history, IAuditLog audit, IEventPublisher events, ICurrentUser user, IClock clock)
{
    public async Task<Result<DelegationResponse>> Handle(Guid casePublicId, UndelegateCaseRequest req, CancellationToken ct)
    {
        var invalid = DelegateCaseValidator.ValidateUndelegate(req);
        if (invalid is not null)
        {
            return invalid;
        }

        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, [Delegation.Permission], ct, Delegation.OnlyActive);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var delegated = await db.CaseInstitutions.SingleOrDefaultAsync(
            l => l.CaseId == c.Id && l.EndedAt == null && l.LinkMode == CaseInstitution.Delegated, ct);
        if (delegated is null)
        {
            return Error.Conflict("case.not_delegated", "Çështja nuk është e deleguar.");
        }

        var institution = (await institutions.GetByIdsAsync([delegated.InstitutionId], ct)).Single();
        var currentCategory = (await reference.ListCaseCategoriesAsync(ct)).FirstOrDefault(x => x.Id == c.CategoryId);
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
        var now = clock.UtcNow;
        var me = (uint)user.UserId;
        try
        {
            delegated.EndedAt = now;
            delegated.EndedBy = me;
            await db.SaveChangesAsync(ct);
            db.CaseInstitutions.Add(new CaseInstitution
            {
                CaseId = c.Id,
                InstitutionId = delegated.InstitutionId,
                LinkMode = CaseInstitution.Monitoring,
                StartedAt = now,
                StartedBy = me
            });
            c.CategoryId = null;
            c.SubcategoryId = null;
            history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.Undelegated,
                $"Delegimi te institucioni {institution.Name} përfundoi; institucioni e monitoron sërish çështjen.",
                old: new { Institution = institution.Name, Category = currentCategory?.Name, Mode = CaseInstitution.Delegated },
                @new: new { Institution = institution.Name, Category = (string?)null, Mode = CaseInstitution.Monitoring, Reason = reason });
            if (await gate.SaveAsync(c, ct) is { } conflict)
            {
                return conflict;
            }
        }
        catch (DbUpdateException ex) when (Delegation.LinkSlotTaken(ex))
        {
            return AssignLawyersHandler.Changed();
        }

        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id,
            details: new { Action = "UNDELEGATE", InstitutionPublicId = institution.PublicId });
        await events.PublishAsync(new CaseUndelegated(c.PublicId, institution.PublicId), ct);
        return new DelegationResponse(c.RowVersion, institution.PublicId, institution.Name, CaseInstitution.Monitoring, null);
    }
}
