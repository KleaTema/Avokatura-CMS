using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Offices;

/// <summary>One active court → office mapping, as the admin screen shows it.</summary>
public sealed record OfficeCourtResponse(int CourtId, Guid OfficePublicId, string OfficeName);

/// <summary>The whole set of courts the office handles (replaces the previous set).</summary>
public sealed record SetOfficeCourtsRequest(IReadOnlyList<int>? CourtIds);

/// <summary>
/// "Gjykatat që mbulon" (task 053c): which office handles which court, so a handoff finds the receiving office.
/// A court maps to at most one ACTIVE office: giving it to this office while another one has it → 409 naming both.
/// Removed courts are kept inactive (is_active = 0), not deleted.
/// </summary>
public sealed class OfficeCourtsHandler(IdentityDbContext db, IReferenceData reference, ICurrentUser user, IAuditLog audit)
{
    /// <summary>Every active mapping (the screen marks the courts other offices hold).</summary>
    public async Task<IReadOnlyList<OfficeCourtResponse>> ListAsync(CancellationToken ct) =>
        await (from oc in db.OfficeCourts.AsNoTracking()
               join o in db.Offices.AsNoTracking() on oc.OfficeId equals o.Id
               where oc.IsActive
               orderby oc.CourtId
               select new OfficeCourtResponse(oc.CourtId, o.PublicId, o.Name))
            .ToListAsync(ct);

    public async Task<Result<IReadOnlyList<OfficeCourtResponse>>> SetAsync(Guid officePublicId, SetOfficeCourtsRequest req, CancellationToken ct)
    {
        var office = await db.Offices.AsNoTracking().SingleOrDefaultAsync(o => o.PublicId == officePublicId, ct);
        if (office is null)
        {
            return OfficeAdminSupport.NotFound();
        }

        var wanted = (req.CourtIds ?? []).Distinct().ToList();
        var courts = (await reference.ListCourtsAsync(ct)).ToDictionary(c => (int)c.Id);
        var unknown = wanted.Where(id => !courts.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            return Error.Validation("office.court_unknown", "Një nga gjykatat nuk ekziston.");
        }

        var taken = await (from oc in db.OfficeCourts.AsNoTracking()
                           join o in db.Offices.AsNoTracking() on oc.OfficeId equals o.Id
                           where oc.IsActive && oc.OfficeId != office.Id && wanted.Contains(oc.CourtId)
                           select new { oc.CourtId, o.Name })
            .ToListAsync(ct);
        if (taken.Count > 0)
        {
            var list = string.Join("; ", taken.Select(t => $"{courts[t.CourtId].Name} — {t.Name}"));
            return Error.Conflict("office.court_taken", $"Këto gjykata mbulohen tashmë nga një zyrë tjetër: {list}.");
        }

        var rows = await db.OfficeCourts.Where(oc => oc.OfficeId == office.Id).ToListAsync(ct);
        var before = rows.Where(r => r.IsActive).Select(r => (int)r.CourtId).OrderBy(x => x).ToList();
        var userId = (uint)user.UserId;
        foreach (var row in rows)
        {
            var active = wanted.Contains(row.CourtId);
            if (row.IsActive != active)
            {
                row.IsActive = active;
                row.UpdatedBy = userId;
            }
        }

        foreach (var courtId in wanted.Where(id => rows.All(r => r.CourtId != id)))
        {
            db.OfficeCourts.Add(new OfficeCourtRecord { OfficeId = office.Id, CourtId = (ushort)courtId, IsActive = true, UpdatedBy = userId });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("uq_oc_active_court", StringComparison.Ordinal) == true)
        {
            // another office took one of these courts at the same moment
            return Error.Conflict("office.court_taken", "Një nga gjykatat u caktua në një zyrë tjetër ndërkohë. Rifreskoni faqen.");
        }

        var after = wanted.OrderBy(x => x).ToList();
        if (!before.SequenceEqual(after))
        {
            OfficeAdminSupport.AuditChange(audit, office, "OFFICE_COURTS_CHANGED", new { Courts = before }, new { Courts = after });
        }

        return (await ListAsync(ct)).Where(m => m.OfficePublicId == office.PublicId).ToList();
    }
}
