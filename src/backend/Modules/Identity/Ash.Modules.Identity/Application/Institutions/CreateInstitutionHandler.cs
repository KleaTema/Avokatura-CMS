using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Identity.Application.Institutions;

public sealed class CreateInstitutionHandler(IdentityDbContext db, IAuditLog audit)
{
    public async Task<Result<InstitutionAdminResponse>> Handle(CreateInstitutionRequest req, CancellationToken ct)
    {
        var invalid = InstitutionAdminSupport.ValidateFields(req.Name, req.ShortName, req.InstitutionType, req.Nipt);
        if (invalid is not null)
        {
            return invalid;
        }

        var type = req.InstitutionType.Trim();
        var nipt = InstitutionAdminSupport.CleanNipt(req.Nipt);
        var duplicate = await InstitutionAdminSupport.ValidateNiptUnique(db, nipt, null, ct);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var parent = await InstitutionAdminSupport.ResolveParentAsync(db, req.ParentInstitutionId, type, ct);
        if (parent.IsFailure)
        {
            return parent.Error!;
        }

        var institution = new InstitutionRecord
        {
            PublicId = Guid.NewGuid(),
            Name = req.Name.Trim(),
            ShortName = InstitutionAdminSupport.CleanShortName(req.ShortName),
            InstitutionType = type,
            ParentInstitutionId = parent.Value,
            Nipt = nipt,
            IsActive = true
        };
        db.Institutions.Add(institution);
        await db.SaveChangesAsync(ct);

        InstitutionAdminSupport.AuditChange(audit, institution, "INSTITUTION_CREATED", null, InstitutionAdminSupport.Snapshot(institution));
        return await InstitutionAdminSupport.ToResponseAsync(db, institution, ct);
    }
}
