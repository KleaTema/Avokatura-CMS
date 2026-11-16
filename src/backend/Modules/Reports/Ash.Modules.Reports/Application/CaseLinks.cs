using Ash.Modules.Cases.Contracts;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Ids;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Application;

/// <summary>
/// Task 053h: a report or drill-down row links to its case only when the user can open it (the case page's own rule,
/// <see cref="ICaseAccessService.CanViewAsync"/>) — e.g. an institution user's statistics count every case of the
/// institution, but a jurist opens only the cases he is assigned to / delegated. Otherwise the code is shown without a
/// link. Checked for one page of rows (at most a few dozen), never for an export.
/// </summary>
public sealed class CaseLinks(ICaseAccessService access, ReportingDb db)
{
    /// <summary>The public ids of the cases among <paramref name="ids"/> the current user can open.</summary>
    public async Task<HashSet<Guid>> OpenableAsync(MySqlConnection connection, IReadOnlyCollection<ulong> ids, CancellationToken ct)
    {
        var openable = new HashSet<Guid>();
        if (ids.Count == 0)
        {
            return openable;
        }

        var cases = await db.QueryAsync<(ulong Id, byte[] PublicId)>(connection,
            "SELECT c.id, c.public_id FROM cases c WHERE c.id IN @ids", new { ids }, false, ct);
        foreach (var (id, publicId) in cases)
        {
            if (await access.CanViewAsync((long)id, ct))
            {
                openable.Add(PublicId.FromBytes(publicId));
            }
        }

        return openable;
    }
}
