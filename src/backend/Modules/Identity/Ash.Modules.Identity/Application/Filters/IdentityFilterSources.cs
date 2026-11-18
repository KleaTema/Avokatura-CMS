using Ash.Modules.Identity.Application.Users;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Filters;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Filters;

/// <summary>
/// Task 053m — the filters of the Identity admin tables. The keys are the pages' URL parameters; the users list sends
/// them to GET /api/identity/admin/users, the offices and institutions (a few dozen rows) match them in the browser.
/// </summary>
internal static class IdentityFilterOptions
{
    public static readonly IReadOnlyList<FilterOption> OfficeTypes =
        [new("CENTRAL", "Qendrore"), new("LOCAL", "Vendore"), new("REPRESENTATION", "E përfaqësimit")];

    public static readonly IReadOnlyList<FilterOption> InstitutionTypes =
    [
        new("MINISTRY", "Ministri"), new("AGENCY", "Agjenci"), new("LOCAL_GOV", "Pushtet vendor"),
        new("INDEPENDENT", "Institucion i pavarur"), new("OTHER", "Tjetër"),
    ];
}

/// <summary>GET /api/filters/users: search, role, active by default; office, institution, position, last login.</summary>
internal sealed class UserListFilterSource(IdentityDbContext db) : ITableFilterSource
{
    public string Table => "users";

    public string? Permission => "admin.users";

    public async Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).Select(r => new FilterOption(r.Code, r.Name)).ToListAsync(ct);
        var offices = await db.Offices.AsNoTracking().OrderBy(o => o.Name).Select(o => new { o.PublicId, o.Name }).ToListAsync(ct);
        // only the institutions that have users (the list holds hundreds)
        var institutions = await db.Institutions.AsNoTracking()
            .Where(i => db.Users.Any(u => u.InstitutionId == i.Id))
            .OrderBy(i => i.Name).Select(i => new { i.PublicId, i.Name }).ToListAsync(ct);

        return
        [
            TableFilter.Of("q", "Kërko (emri, mbiemri, email)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("role", "Roli", FilterKinds.Select, isDefault: true, options: roles),
            TableFilter.Of("active", "Aktiv", FilterKinds.YesNo, isDefault: true),
            TableFilter.Of("office", "Zyra", FilterKinds.Select,
                options: offices.Select(o => new FilterOption(o.PublicId.ToString(), o.Name)).ToList()),
            TableFilter.Of("institution", "Institucioni", FilterKinds.Select,
                options: institutions.Select(i => new FilterOption(i.PublicId.ToString(), i.Name)).ToList()),
            TableFilter.Of("position", "Pozicioni", FilterKinds.Text),
            TableFilter.Range("lastLogin", "Hyrja e fundit", FilterKinds.DateRange, "lastLoginFrom", "lastLoginTo"),
            // task 053n: what the admin dashboard's cards open
            TableFilter.Of("noRole", "Pa asnjë rol", FilterKinds.YesNo),
            TableFilter.Of("neverLoggedIn", "Nuk ka hyrë asnjëherë", FilterKinds.YesNo),
            TableFilter.Range("created", "Krijuar më", FilterKinds.DateRange, "createdFrom", "createdTo"),
            TableFilter.Of("login", "Llogaria e hyrjes", FilterKinds.Select, options:
            [
                new(UserLoginFilter.Linked, "E lidhur"), new(UserLoginFilter.None, "Pa llogari hyrjeje"),
                new(UserLoginFilter.Blocked, "E bllokuar"),
            ]),
        ];
    }
}

/// <summary>GET /api/filters/offices: search, type, active by default; the parent office.</summary>
internal sealed class OfficeListFilterSource(IdentityDbContext db) : ITableFilterSource
{
    public string Table => "offices";

    public string? Permission => "admin.org";

    public async Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var parents = await db.Offices.AsNoTracking()
            .Where(o => db.Offices.Any(child => child.ParentOfficeId == o.Id))
            .OrderBy(o => o.Name).Select(o => new { o.PublicId, o.Name }).ToListAsync(ct);

        return
        [
            TableFilter.Of("q", "Kërko (kodi, emri, kryetari)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("type", "Lloji", FilterKinds.Select, isDefault: true, options: IdentityFilterOptions.OfficeTypes),
            TableFilter.Of("active", "Aktive", FilterKinds.YesNo, isDefault: true),
            TableFilter.Of("parent", "Zyra mëmë", FilterKinds.Select,
                options: parents.Select(o => new FilterOption(o.PublicId.ToString(), o.Name)).ToList()),
            // task 053n: "Zyrat pa Kryetar" on the admin dashboard opens head=false
            TableFilter.Of("head", "Ka Kryetar", FilterKinds.YesNo),
        ];
    }
}

/// <summary>GET /api/filters/institutions (a server-paged list): search, type, active by default; the ministry.</summary>
internal sealed class InstitutionListFilterSource(IdentityDbContext db) : ITableFilterSource
{
    public string Table => "institutions";

    public string? Permission => "admin.org";

    public async Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var ministries = await db.Institutions.AsNoTracking()
            .Where(i => i.InstitutionType == "MINISTRY")
            .OrderBy(i => i.Name).Select(i => new { i.PublicId, i.Name }).ToListAsync(ct);

        return
        [
            TableFilter.Of("q", "Kërko (emri, shkurtimi, NIPT)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("type", "Lloji", FilterKinds.Select, isDefault: true, options: IdentityFilterOptions.InstitutionTypes),
            TableFilter.Of("active", "Aktive", FilterKinds.YesNo, isDefault: true),
            TableFilter.Of("parent", "Ministria", FilterKinds.Select,
                options: ministries.Select(i => new FilterOption(i.PublicId.ToString(), i.Name)).ToList()),
        ];
    }
}
