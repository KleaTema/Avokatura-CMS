using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Dashboard;

/// <summary>
/// Task 053n — the administrator's widgets: the administration, never cases or offices' numbers (the administrator is
/// not tied to an office). Every count is <see cref="IAdminOverview"/>'s query of the admin page its LinkPath opens with
/// that filter, so the card and the list agree. Job and partition health are no widgets: they stay in /health/ready
/// and the logs (IT operations).
/// </summary>
internal sealed partial class DashboardWidgets
{
    // ---------------------------------------------------------------- users, offices, courts

    private static DashboardWidget AdminCount(string code, string title, long value, string link) =>
        new(code, DashboardWidget.Count, title, value, LinkPath: link);

    public async Task<DashboardWidget> AdminUsersAsync(string code, DateTime now, CancellationToken ct)
    {
        var monthStart = new DateOnly(TiraneTime.DateOf(now).Year, TiraneTime.DateOf(now).Month, 1);
        return code switch
        {
            "adminUsersActive" => AdminCount(code, "Përdorues aktivë",
                await admin.CountUsersAsync(AdminUserCount.Active, monthStart, ct), "/admin/users?active=true"),
            "adminUsersInactive" => AdminCount(code, "Përdorues joaktivë",
                await admin.CountUsersAsync(AdminUserCount.Inactive, monthStart, ct), "/admin/users?active=false"),
            "adminUsersNoRole" => AdminCount(code, "Pa asnjë rol",
                await admin.CountUsersAsync(AdminUserCount.NoRole, monthStart, ct), "/admin/users?noRole=true"),
            "adminUsersNeverLoggedIn" => AdminCount(code, "Nuk kanë hyrë asnjëherë",
                await admin.CountUsersAsync(AdminUserCount.NeverLoggedIn, monthStart, ct), "/admin/users?neverLoggedIn=true"),
            "adminUsersCreatedThisMonth" => AdminCount(code, "Krijuar këtë muaj",
                await admin.CountUsersAsync(AdminUserCount.CreatedSince, monthStart, ct), $"/admin/users?createdFrom={Day(monthStart)}"),
            "adminLoginBlocked" => AdminCount(code, "Me llogari hyrjeje të bllokuar",
                await admin.CountUsersAsync(AdminUserCount.LoginBlocked, monthStart, ct), "/admin/users?login=BLOCKED"),
            "adminLoginNone" => AdminCount(code, "Pa llogari hyrjeje",
                await admin.CountUsersAsync(AdminUserCount.LoginNone, monthStart, ct), "/admin/users?login=NONE"),
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
    }

    public async Task<DashboardWidget> AdminOfficesWithoutHeadAsync(DateTime now, CancellationToken ct) =>
        AdminCount("adminOfficesNoHead", "Zyra pa Kryetar", await admin.CountOfficesWithoutHeadAsync(ct),
            "/admin/offices?active=true&head=false");

    /// <summary>Active courts no active office covers (office_courts): a new case there has nowhere to go.</summary>
    public async Task<DashboardWidget> AdminCourtsUncoveredAsync(DateTime now, CancellationToken ct)
    {
        var covered = await admin.ListCoveredCourtIdsAsync(ct);
        var uncovered = (await reference.ListCourtsAsync(ct)).Count(c => c.IsActive && !covered.Contains(c.Id));
        return AdminCount("adminCourtsUncovered", "Gjykata pa zyrë", uncovered, "/admin/lists?list=courts&active=true&covered=false");
    }

    // ---------------------------------------------------------------- imports

    /// <summary>The imports with errors (Organika, users): Value = how many; each row opens its history, "Me gabime".</summary>
    public async Task<DashboardWidget> AdminImportErrorsAsync(DateTime now, CancellationToken ct)
    {
        var imports = await admin.ListImportsWithErrorsAsync(ct);
        var items = imports.Take(ListSize).Select(i => new DashboardItem(
                i.FileName,
                Detail: i.Kind == "ORGANIKA" ? "Organika" : "Përdoruesit",
                At: i.UploadedAt,
                Value: i.Errors,
                LinkPath: i.Kind == "ORGANIKA" ? "/admin/organika?impErrors=true" : "/admin/users?import=users&impErrors=true"))
            .ToList();
        return new DashboardWidget("adminImportErrors", DashboardWidget.List, "Importet me gabime", imports.Count, Items: items);
    }

    // ---------------------------------------------------------------- recent administrative changes

    /// <summary>A row of ash_audit.v_admin_changes (script 033): the app account's only read of the audit log.</summary>
    private sealed class AdminChangeRow
    {
        public ulong Id { get; set; }
        public DateTime OccurredAt { get; set; }
        public uint? ActorUserId { get; set; }
        public string? ActorDisplay { get; set; }
        public string? EntityType { get; set; }
        public ulong? EntityId { get; set; }
        public string? ChangeAction { get; set; }
    }

    private static readonly IReadOnlyDictionary<string, string> EntityNames = new Dictionary<string, string>
    {
        ["USER"] = "Përdoruesi",
        ["OFFICE"] = "Zyra",
        ["INSTITUTION"] = "Institucioni",
        ["COURT"] = "Gjykata",
        ["JUDGE"] = "Gjyqtari",
        ["HEARING_TYPE"] = "Lloji i seancës",
        ["PARTY_ROLE"] = "Roli i palës",
        ["SUMMONS_TYPE"] = "Lloji i fletëthirrjes",
        ["DOCUMENT_TYPE"] = "Lloji i dokumentit",
        ["DECISION_RESULT"] = "Rezultati i vendimit",
        ["FILE_TYPE"] = "Lloji i skedarit",
        ["DOSSIER_CODE_FORMAT"] = "Formati i kodit të dosjes",
        ["IMPORT"] = "Importi",
        ["SUPERVISION"] = "Mbikëqyrja",
        ["ASSISTANT_LINK"] = "Lidhja e ndihmësit ligjor",
        ["STATISTICS_SNAPSHOT"] = "Statistikat e vitit",
    };

    /// <summary>What happened, in Albanian, from details.action (…_CREATED, ROLE_GRANTED …).</summary>
    private static string ActionText(string? action) => action switch
    {
        null or "" => "u ndryshua",
        "ROLE_GRANTED" => "iu dha një rol",
        "ROLE_REVOKED" => "iu hoq një rol",
        "OFFICE_HEAD_CHANGED" => "iu ndryshua Kryetari",
        "OFFICE_COURTS_CHANGED" => "iu ndryshuan gjykatat",
        _ when action.EndsWith("_DEACTIVATED", StringComparison.Ordinal) => "u çaktivizua",
        _ when action.EndsWith("_ACTIVATED", StringComparison.Ordinal) => "u aktivizua",
        _ when action.EndsWith("_CREATED", StringComparison.Ordinal) => "u krijua",
        _ => "u ndryshua",
    };

    /// <summary>
    /// The last 10 ADMIN_CHANGE rows: Label = what (the item and the change), Detail = who, At = when. A user, office or
    /// institution links to its admin page filtered to it (inactive ones included); anything else is plain text.
    /// </summary>
    public async Task<DashboardWidget> AdminRecentChangesAsync(DateTime now, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<AdminChangeRow>(
                "SELECT id AS Id, occurred_at AS OccurredAt, actor_user_id AS ActorUserId, actor_display AS ActorDisplay, " +
                "entity_type AS EntityType, entity_id AS EntityId, change_action AS ChangeAction " +
                "FROM ash_audit.v_admin_changes ORDER BY occurred_at DESC, id DESC LIMIT 10")
            .ToListAsync(ct);

        // the public ids and names of the 10 rows' items, one query per kind (entity_id never leaves the server)
        var userIds = IdsOf("USER");
        var userRefs = userIds.Count > 0 ? await users.GetRefsAsync(userIds, ct) : new Dictionary<long, UserRef>();
        var institutionInfos = IdsOf("INSTITUTION") is { Count: > 0 } institutionIds
            ? (await institutions.GetByIdsAsync(institutionIds, ct)).ToDictionary(i => i.Id)
            : new Dictionary<long, InstitutionInfo>();
        var officeInfos = new Dictionary<long, OfficeInfo>();
        foreach (var id in IdsOf("OFFICE"))
        {
            if (await offices.GetByIdAsync(id, ct) is { } office)
            {
                officeInfos[id] = office;
            }
        }

        var items = rows.Select(r =>
        {
            var kind = r.EntityType ?? string.Empty;
            var id = (long)(r.EntityId ?? 0);
            (string? name, string? link) = kind switch
            {
                "USER" when userRefs.TryGetValue(id, out var u) => (u.Name, $"/admin/users?userId={u.PublicId}"),
                "OFFICE" when officeInfos.TryGetValue(id, out var o) => (o.Name, $"/admin/offices?office={o.PublicId}"),
                "INSTITUTION" when institutionInfos.TryGetValue(id, out var i) => (i.Name, $"/admin/institutions?institutionId={i.PublicId}"),
                _ => ((string?)null, (string?)null),
            };
            var what = EntityNames.GetValueOrDefault(kind, kind);
            var label = name is null ? $"{what} {ActionText(r.ChangeAction)}" : $"{what} {name} {ActionText(r.ChangeAction)}";
            return new DashboardItem(label, Detail: r.ActorDisplay ?? "Sistemi",
                At: DateTime.SpecifyKind(r.OccurredAt, DateTimeKind.Utc), LinkPath: link);
        }).ToList();
        return new DashboardWidget("adminRecentChanges", DashboardWidget.List, "Ndryshimet e fundit administrative", items.Count, Items: items);

        List<long> IdsOf(string type) =>
            rows.Where(r => r.EntityType == type && r.EntityId != null).Select(r => (long)r.EntityId!.Value).Distinct().ToList();
    }
}
