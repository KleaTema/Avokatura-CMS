using System.Text.Json.Nodes;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.History;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.GetHistory;

/// <summary>
/// A history row. Level = the case code (X/n) of the level, or the dossier's unique code for dossier-wide rows.
/// Old/New are the changed fields, without internal ids.
/// </summary>
public sealed record CaseHistoryItem(
    string Level, string EventType, string? ActorName, DateTime OccurredAt, string Summary, JsonNode? Old, JsonNode? New);

/// <summary>
/// GET /api/cases/{id}/history: the history of the WHOLE dossier, newest first (keyset on occurred_at, id), limited to
/// the levels the user can view — rows of a level without access are skipped, dossier-wide rows are kept.
/// </summary>
internal sealed class GetHistoryHandler(CasesDbContext db, ICaseAccessService access, IUserDirectory users)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public async Task<Result<KeysetPage<CaseHistoryItem>>> Handle(Guid casePublicId, string? after, int? limit, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        }

        (DateTime At, ulong Id)? position = null;
        if (after is not null)
        {
            if (!Cursor.TryDecode(after, out var key, out var id) || id <= 0
                || !long.TryParse(key, out var ticks) || ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
            {
                var errors = new FieldErrors();
                errors.Add("after", "Kursori nuk është i vlefshëm; filloni nga faqja e parë.");
                return errors.ToError("Kërkimi nuk është i vlefshëm.");
            }

            position = (new DateTime(ticks, DateTimeKind.Utc), (ulong)id);
        }

        var levels = await db.Cases.AsNoTracking()
            .Where(x => x.DossierId == c.DossierId)
            .Select(x => new { x.Id, x.CaseCode })
            .ToListAsync(ct);
        var visible = new List<ulong>(levels.Count);
        foreach (var level in levels)
        {
            if (await access.CanViewAsync((long)level.Id, ct))
            {
                visible.Add(level.Id);
            }
        }

        var codes = levels.ToDictionary(x => x.Id, x => x.CaseCode);
        var dossierCode = await db.Dossiers.AsNoTracking()
            .Where(d => d.Id == c.DossierId).Select(d => d.UniqueCode).SingleAsync(ct);

        // ix_che_dossier (dossier_id, occurred_at)
        var rows = db.CaseHistoryEvents.AsNoTracking()
            .Where(e => e.DossierId == c.DossierId && (e.CaseId == null || visible.Contains(e.CaseId.Value)));
        if (position is { } p)
        {
            rows = rows.Where(e => e.OccurredAt < p.At || (e.OccurredAt == p.At && e.Id < p.Id));
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var page = await rows.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Take(take + 1).ToListAsync(ct);
        var hasMore = page.Count > take;
        page = page.Take(take).ToList();

        var actors = await users.GetNamesAsync(
            page.Where(e => e.ActorUserId != null).Select(e => (long)e.ActorUserId!.Value).Distinct().ToList(), ct);
        var items = page.Select(e =>
        {
            var (old, @new) = HistoryDetails.Read(e.Details);
            return new CaseHistoryItem(
                e.CaseId is { } caseId ? codes.GetValueOrDefault(caseId, dossierCode) : dossierCode,
                e.EventType,
                e.ActorUserId is { } actor ? actors.GetValueOrDefault(actor) : null,
                e.OccurredAt,
                e.Summary,
                old,
                @new);
        }).ToList();

        var next = hasMore ? Cursor.Encode(page[^1].OccurredAt.Ticks.ToString(), (long)page[^1].Id) : null;
        return new KeysetPage<CaseHistoryItem>(items, next);
    }
}
