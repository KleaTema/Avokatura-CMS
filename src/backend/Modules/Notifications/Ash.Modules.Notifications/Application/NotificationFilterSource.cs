using Ash.Modules.Notifications.Infrastructure;
using Ash.SharedKernel.Filters;
using Ash.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Notifications.Application;

/// <summary>
/// Task 053m — GET /api/filters/notifications: the user's own notifications. Defaults: read state (?show=unread, the
/// page's existing parameter) and the type (only the types the user has); "+ Shto filtër": a text of the title, the
/// date. The keys are the page's URL parameters; the page maps them to GET /api/notifications.
/// </summary>
internal sealed class NotificationFilterSource(NotificationsDbContext db, ICurrentUser user) : ITableFilterSource
{
    public string Table => "notifications";

    public string? Permission => null;

    /// <summary>The types' names (the same as the profile's switches, sq.profile.types).</summary>
    private static readonly IReadOnlyDictionary<string, string> TypeNames = new Dictionary<string, string>
    {
        ["CASE_REGISTERED"] = "Çështje e re për caktim",
        ["LAWYER_ASSIGNED"] = "Caktimi si avokat në një çështje",
        ["LAWYER_REMOVED"] = "Heqja si avokat nga një çështje",
        ["INSTITUTION_CASE_ASSIGNED"] = "Avokatët e caktuar në çështjet e institucionit",
        ["ASSISTANT_ASSIGNED"] = "Caktimi si ndihmës ligjor",
        ["ASSISTANT_REMOVED"] = "Heqja si ndihmës ligjor",
        ["JURIST_ASSIGNED"] = "Caktimi si jurist i institucionit",
        ["JURIST_REPLACED"] = "Zëvendësimi si jurist",
        ["INSTITUTION_LINKED"] = "Lidhja e institucionit me një çështje",
        ["CASE_DELEGATED"] = "Delegimi i një çështjeje",
        ["CASE_UNDELEGATED"] = "Heqja e delegimit",
        ["PREVIOUS_LEVEL_NOT_CLOSED"] = "Shkalla e mëparshme nuk është mbyllur",
        ["CASE_CLOSED"] = "Mbyllja e një çështjeje",
        ["DOCUMENT_INFECTED"] = "Dokument i infektuar",
        ["DOCUMENT_SCAN_FAILED"] = "Kontrolli i dokumentit dështoi",
        ["DATA_MISSING_30D"] = "Mungojnë të dhënat pas 30 ditësh",
        ["DATA_MISSING_6M"] = "Mungojnë të dhënat pas 6 muajsh",
        ["HEARING_TOMORROW"] = "Seancë nesër",
        ["NEXT_LEVEL_PENDING_3D"] = "Fletëthirrje e shkallës tjetër pas 3 ditësh",
        ["PARTITION_ALERT"] = "Alarm për particionet e bazës së të dhënave",
        ["CASE_HANDED_OFF"] = "Çështje e përcjellë te zyra juaj",
        ["HANDOFF_WITHDRAWN"] = "Përcjellja e çështjes u anulua",
        ["SUPERVISION_ASSIGNED"] = "Mbikëqyrje e re",
        ["SUPERVISION_ENDED"] = "Mbikëqyrja përfundoi",
    };

    public async Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var userId = (uint)user.UserId;
        var types = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .Select(n => n.NotificationType).Distinct().ToListAsync(ct);

        return
        [
            TableFilter.Of("show", "Shfaq", FilterKinds.Select, isDefault: true, options: [new("unread", "Të palexuara")]),
            TableFilter.Of("type", "Lloji", FilterKinds.Select, isDefault: true,
                options: types.Select(t => new FilterOption(t, TypeNames.GetValueOrDefault(t, t)))
                    .OrderBy(o => o.Label).ToList()),
            TableFilter.Of("q", "Teksti i titullit", FilterKinds.Text),
            TableFilter.Range("created", "Data", FilterKinds.DateRange, "from", "to"),
        ];
    }
}
