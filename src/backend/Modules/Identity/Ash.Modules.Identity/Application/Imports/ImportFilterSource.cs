using Ash.SharedKernel.Filters;

namespace Ash.Modules.Identity.Application.Imports;

/// <summary>
/// Task 053m — GET /api/filters/imports: the history of an import (Organika, users; the last 100, filtered in the
/// browser). Defaults: the file / uploader, applied or not; "+ Shto filtër": the upload days. The keys carry an "imp"
/// prefix: the panel also opens inside the users page, whose own filters share the URL.
/// </summary>
internal sealed class ImportFilterSource : ITableFilterSource
{
    public string Table => "imports";

    public string? Permission => "admin.users";

    public Task<IReadOnlyList<TableFilter>> DescribeAsync(IReadOnlyDictionary<string, string> context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TableFilter>>(
        [
            TableFilter.Of("impQ", "Kërko (skedari, ngarkuar nga)", FilterKinds.Text, isDefault: true),
            TableFilter.Of("impApplied", "Aplikuar", FilterKinds.YesNo, isDefault: true),
            TableFilter.Range("impUploaded", "Ngarkuar më", FilterKinds.DateRange, "impFrom", "impTo"),
        ]);
}
