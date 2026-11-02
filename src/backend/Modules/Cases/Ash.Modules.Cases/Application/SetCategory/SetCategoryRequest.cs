namespace Ash.Modules.Cases.Application.SetCategory;

/// <summary>PATCH /api/cases/{id}/category — by code; the subcategory is required when the category has any.</summary>
public sealed record SetCategoryRequest(string? CategoryCode, string? SubcategoryCode, uint? RowVersion);
