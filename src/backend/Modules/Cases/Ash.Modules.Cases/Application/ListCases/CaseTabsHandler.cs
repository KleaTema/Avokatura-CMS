using Ash.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.ListCases;

/// <summary>A tab of the user with the number of open cases in it (for badges like "Pa avokat (3)").</summary>
public sealed record CaseTabCount(string Tab, int Count);

public sealed record CaseTabsResponse(IReadOnlyList<CaseTabCount> Tabs, string? DefaultTab);

/// <summary>The user's tabs with their open-case counts and the tab to open first.</summary>
internal sealed class CaseTabsHandler(CaseListQueryBuilder builder, Handoff.HandoffQueries handoffs, ICurrentUser user)
{
    /// <summary>
    /// "Çështjet e ardhura" (task 053c): the open handoffs to the Protokoll's office. Not a case list (the user cannot
    /// open those cases): the page shows GET /api/cases/handoffs/incoming instead of the table.
    /// </summary>
    public const string IncomingTab = "incoming";

    public async Task<CaseTabsResponse> Handle(CancellationToken ct)
    {
        var tabs = CaseListTabs.For(user);
        var counts = new List<CaseTabCount>(tabs.Count + 1);
        foreach (var tab in tabs)
        {
            counts.Add(new CaseTabCount(CaseListTabs.Code(tab), await builder.Filtered(CaseListFilter.Open(tab)).CountAsync(ct)));
            if (tab == CaseListTab.Registered && user.HasPermission(Handoff.HandoffQueries.LinkPreviousPermission))
            {
                counts.Add(new CaseTabCount(IncomingTab, await handoffs.IncomingCountAsync(user.OfficeId!.Value, ct)));
            }
        }

        var unassigned = counts.FirstOrDefault(c => c.Tab == CaseListTabs.Code(CaseListTab.Unassigned))?.Count ?? 0;
        var defaultTab = CaseListTabs.Default(tabs, unassigned);
        return new CaseTabsResponse(counts, defaultTab is { } d ? CaseListTabs.Code(d) : null);
    }
}
