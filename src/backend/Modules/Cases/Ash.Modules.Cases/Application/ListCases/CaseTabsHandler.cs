using Ash.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.ListCases;

/// <summary>A tab of the user with the number of cases it lists with no filter (for badges like "Pa avokat (3)").</summary>
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
            // task 054c: the count is what the tab lists with no filter
            counts.Add(new CaseTabCount(CaseListTabs.Code(tab), await builder.Filtered(CaseListFilter.Whole(tab)).CountAsync(ct)));
        }

        // "Pa avokat" holds REGISTERED cases only: its whole count is its open count
        var unassigned = counts.FirstOrDefault(c => c.Tab == CaseListTabs.Code(CaseListTab.Unassigned))?.Count ?? 0;
        var defaultTab = CaseListTabs.Default(tabs, unassigned);
        return new CaseTabsResponse(counts, defaultTab is { } d ? CaseListTabs.Code(d) : null);
    }
}
