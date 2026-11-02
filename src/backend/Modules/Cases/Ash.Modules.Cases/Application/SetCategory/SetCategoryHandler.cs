using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.SetCategory;

/// <summary>
/// PATCH category (case.set_category, the primary): a category that is not a delegation (NDJEKJE) with its
/// subcategory, both by code from the lookup tables. Delegation categories (is_delegation: DELEGIM / RELATIM) are set
/// and ended only by the delegation action (task 027): requesting one → 400, a delegated case → 409.
/// </summary>
internal sealed class SetCategoryHandler(
    CaseDataGate gate, FirstDataEntry firstEntry, IReferenceData reference, IHistoryWriter history, IAuditLog audit)
{
    public const string Permission = "case.set_category";

    public async Task<Result<SetCategoryResponse>> Handle(Guid casePublicId, SetCategoryRequest req, CancellationToken ct)
    {
        var invalid = SetCategoryValidator.Validate(req);
        if (invalid is not null)
        {
            return invalid;
        }

        // a delegated case: the category belongs to the delegation (task 027) — checked before the permission, which
        // the effective permissions drop in that state
        var categories = await reference.ListCaseCategoriesAsync(ct);
        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, [Permission], ct,
            c => Task.FromResult(categories.Any(x => x.Id == c.CategoryId && x.IsDelegation)
                ? Error.Conflict("case.delegated", "Çështja është e deleguar; ndryshimi bëhet te delegimi.")
                : null));
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var subcategories = await reference.ListCaseSubcategoriesAsync(ct);

        // the requested pair
        var errors = new FieldErrors();
        var category = categories.FirstOrDefault(x => x.Code == req.CategoryCode!.Trim());
        CaseSubcategoryDto? subcategory = null;
        if (category is null)
        {
            errors.Add("categoryCode", "Kategoria nuk ekziston.");
        }
        else if (category.IsDelegation)
        {
            errors.Add("categoryCode", "Delegimi/relatimi caktohet nga veprimi i delegimit.");
        }
        else
        {
            var own = subcategories.Where(s => s.CategoryId == category.Id).ToList();
            var code = req.SubcategoryCode?.Trim();
            if (own.Count > 0 && string.IsNullOrEmpty(code))
            {
                errors.Add("subcategoryCode", "Zgjidhni nënkategorinë.");
            }
            else if (!string.IsNullOrEmpty(code))
            {
                subcategory = own.FirstOrDefault(s => s.Code == code);
                if (subcategory is null)
                {
                    errors.Add("subcategoryCode", "Nënkategoria nuk i përket kësaj kategorie.");
                }
            }
        }

        if (errors.Any)
        {
            return errors.ToError(SetCategoryValidator.InvalidMessage);
        }

        var current = categories.FirstOrDefault(x => x.Id == c.CategoryId);
        if (c.CategoryId == category!.Id && c.SubcategoryId == subcategory?.Id)
        {
            return CaseDataGate.NoChange();
        }

        var currentSub = subcategories.FirstOrDefault(s => s.Id == c.SubcategoryId);
        c.CategoryId = category.Id;
        c.SubcategoryId = subcategory?.Id;
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.CategoryChanged,
            subcategory is null
                ? $"Kategoria u ndryshua: {category.Name}."
                : $"Kategoria u ndryshua: {category.Name} — {subcategory.Name}.",
            old: new { Category = current?.Name, Subcategory = currentSub?.Name },
            @new: new { Category = category.Name, Subcategory = subcategory?.Name });
        if (await gate.SaveAsync(c, ct) is { } conflict)
        {
            return conflict;
        }

        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id, details: new { Action = "SET_CATEGORY", category.Code });
        return new SetCategoryResponse(c.RowVersion, new CodeName(category.Code, category.Name),
            subcategory is null ? null : new CodeName(subcategory.Code, subcategory.Name));
    }
}
