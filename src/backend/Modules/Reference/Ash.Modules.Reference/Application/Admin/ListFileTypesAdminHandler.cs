using Ash.Modules.Reference.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class ListFileTypesAdminHandler(ReferenceDbContext db)
{
    public async Task<IReadOnlyList<FileTypeAdminResponse>> Handle(CancellationToken ct)
        => await db.AllowedFileTypes.AsNoTracking()
            .OrderBy(x => x.Extension)
            .ThenBy(x => x.AppliesTo)
            .Select(x => FileTypeAdminSupport.ToResponse(x))
            .ToListAsync(ct);
}
