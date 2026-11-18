using Ash.Modules.Cases.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Filters;

namespace Ash.Modules.Documents.Application.List;

/// <summary>
/// Task 053m — GET /api/filters/documents?case={publicId}: the filters of a dossier's document timeline. Defaults: type,
/// level, status; "+ Shto filtër": the title, the upload days. The URL parameters carry the tab's "docs" prefix (the case
/// page holds other tabs' parameters too); the levels are the ones the user can view (as the list itself).
/// </summary>
internal sealed class DocumentFilterSource(ICaseActivity cases, ICaseAccessService access, IReferenceData reference)
    : ITableFilterSource
{
    public string Table => "documents";

    public string? Permission => null;

    public async Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct)
    {
        var levels = new List<FilterOption>();
        if (context.TryGetValue("case", out var id) && Guid.TryParse(id, out var casePublicId)
            && await cases.FindViewableAsync(casePublicId, ct) is { } c)
        {
            foreach (var level in await cases.ListLevelsAsync(c.DossierId, ct))
            {
                if (await access.CanViewAsync(level.CaseId, ct))
                {
                    levels.Add(new FilterOption(level.LevelSeq.ToString(System.Globalization.CultureInfo.InvariantCulture), level.CaseCode));
                }
            }
        }

        var types = (await reference.ListDocumentTypesAsync(ct)).OrderBy(t => t.SortOrder).ThenBy(t => t.Name);
        return
        [
            new TableFilter("type", "Lloji", FilterKinds.Select, null, true, ["docsType"],
                types.Select(t => new FilterOption(t.Code, t.Name)).ToList()),
            new TableFilter("level", "Shkalla", FilterKinds.Select, null, true, ["docsLevel"], levels),
            new TableFilter("status", "Statusi", FilterKinds.Select, null, true, ["docsStatus"],
                [new("PENDING", "Në kontroll"), new("CLEAN", "Gati"), new("REJECTED", "Refuzuar")]),
            new TableFilter("q", "Titulli", FilterKinds.Text, null, false, ["docsQ"], null),
            new TableFilter("uploaded", "Ngarkuar më", FilterKinds.DateRange, null, false, ["docsFrom", "docsTo"], null),
        ];
    }
}
