using Ash.Modules.Reference.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class ListDossierCodeFormatsAdminHandler(ReferenceDbContext db)
{
    public async Task<IReadOnlyList<DossierCodeFormatAdminResponse>> Handle(CancellationToken ct)
        => await db.DossierCodeFormats.AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Id)
            .Select(x => DossierCodeFormatAdminSupport.ToResponse(x))
            .ToListAsync(ct);
}
