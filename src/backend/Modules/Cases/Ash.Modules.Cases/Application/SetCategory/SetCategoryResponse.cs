using Ash.Modules.Cases.Application.GetCase;

namespace Ash.Modules.Cases.Application.SetCategory;

public sealed record SetCategoryResponse(uint RowVersion, CodeName Category, CodeName? Subcategory);
