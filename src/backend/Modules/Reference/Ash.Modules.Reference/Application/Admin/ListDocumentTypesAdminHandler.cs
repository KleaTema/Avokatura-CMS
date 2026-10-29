using Ash.Modules.Reference.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class ListDocumentTypesAdminHandler(ReferenceDbContext db)
{
    public async Task<IReadOnlyList<DocumentTypeAdminResponse>> Handle(CancellationToken ct)
    {
        return await db.DocumentTypes.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .Select(x => new DocumentTypeAdminResponse(x.Code, x.Name, x.IsOther, x.SortOrder, x.IsActive))
            .ToListAsync(ct);
    }
}
