using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Cases.Infrastructure.History;

public sealed class HistoryWriter(CasesDbContext db, ICurrentUser user, IClock clock) : IHistoryWriter
{
    public const int MaxSummaryLength = 500;

    public void Add(long dossierId, long? caseId, HistoryEventType type, string summary, object? old = null, object? @new = null)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("History summary is required.", nameof(summary));
        }

        if (summary.Length > MaxSummaryLength)
        {
            throw new ArgumentException($"History summary is longer than {MaxSummaryLength} characters.", nameof(summary));
        }

        db.CaseHistoryEvents.Add(new CaseHistoryEvent
        {
            DossierId = (ulong)dossierId,
            CaseId = caseId is { } id ? (ulong)id : null,
            EventType = HistoryEventCodes.ToCode(type),
            ActorUserId = CurrentUserId(),
            OccurredAt = clock.UtcNow,
            Summary = summary,
            Details = HistoryDetails.Build(old, @new),
        });
    }

    // Background jobs have no user: the scoped CurrentUser throws when nobody was resolved, which means "system".
    private uint? CurrentUserId()
    {
        try
        {
            return (uint)user.UserId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
