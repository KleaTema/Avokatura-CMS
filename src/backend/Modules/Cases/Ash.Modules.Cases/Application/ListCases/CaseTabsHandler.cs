using Ash.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.ListCases;

/// <summary>A tab of the user with the number of open cases in it (for badges like "Pa avokat (3)").</summary>
public sealed record CaseTabCount(string Tab, int Count);

public sealed record CaseTabsResponse(IReadOnlyList<CaseTabCount> Tabs, string? DefaultTab);

/// <summary>The user's tabs with their open-case counts and the tab to open first.</summary>
internal sealed class CaseTabsHandler(CaseListQueryBuilder builder, ICurrentUser user)
{
    public async Task<CaseTabsResponse> Handle(CancellationToken ct)
    {
        var tabs = CaseListTabs.For(user);
        var counts = new List<CaseTabCount>(tabs.Count);
        foreach (var tab in tabs)
        {
            counts.Add(new CaseTabCount(CaseListTabs.Code(tab), await builder.Filtered(CaseListFilter.Open(tab)).CountAsync(ct)));
        }

        var unassigned = counts.FirstOrDefault(c => c.Tab == CaseListTabs.Code(CaseListTab.Unassigned))?.Count ?? 0;
        var defaultTab = CaseListTabs.Default(tabs, unassigned);
        return new CaseTabsResponse(counts, defaultTab is { } d ? CaseListTabs.Code(d) : null);
    }
}
