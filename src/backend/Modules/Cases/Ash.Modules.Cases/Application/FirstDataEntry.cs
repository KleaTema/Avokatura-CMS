using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application;

/// <summary>
/// cases.first_data_entry_at: the team's first data entry, which stops the 30-day reminder that starts at assigned_at.
/// Set once, by a write of someone working ON the case: a current assignment (primary, secondary, legal assistant,
/// institution jurist) or a user of a DELEGATED institution. The Kryetar's or Protokoll's own actions (assigning,
/// priority as head, registration fixes) do not count — unless that person is on the team.
/// Called by every team write: case data (task 025) and parties (017).
/// TODO(task-030, task-033): hearings and documents must call it too.
/// </summary>
internal sealed class FirstDataEntry(CasesDbContext db, ICurrentUser user, IClock clock)
{
    private const string DelegatedProfile = "INSTITUTION_DELEGATED";

    /// <summary>
    /// Call after a successful write, in the same transaction. An UPDATE of its own (… AND first_data_entry_at IS NULL),
    /// so it neither touches the tracked case nor its row_version.
    /// </summary>
    public async Task MarkAsync(Case c, CancellationToken ct)
    {
        if (c.FirstDataEntryAt is not null)
        {
            return;
        }

        var userId = (uint)user.UserId;
        var onTeam = await db.CaseAssignments.AnyAsync(a => a.CaseId == c.Id && a.UserId == userId && a.EndedAt == null, ct)
                     || await db.CaseAccess.AnyAsync(a => a.CaseId == c.Id && a.UserId == userId && a.AccessProfile == DelegatedProfile, ct);
        if (!onTeam)
        {
            return;
        }

        var now = clock.UtcNow;
        await db.Cases
            .Where(x => x.Id == c.Id && x.FirstDataEntryAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FirstDataEntryAt, now), ct);
    }
}
