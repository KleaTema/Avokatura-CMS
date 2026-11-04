using System.Globalization;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Notifications.Infrastructure;
using Ash.Modules.Notifications.Realtime;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Notifications.Application;

/// <summary>A notification of the current user. Public ids only; the type is a NotificationTypes code.</summary>
public sealed record NotificationItem(
    Guid PublicId, string Type, string Title, string? Body, string? LinkPath, DateTime CreatedAt, DateTime? ReadAt);

public sealed record UnreadCountResponse(int Unread);

public sealed record ReadAllResponse(int Marked, int Unread);

/// <summary>
/// The current user's notifications — never anyone else's: every query is filtered by the resolved user's id, so
/// another user's public id is simply not found (404, nothing about whether it exists).
/// </summary>
internal sealed class NotificationQueries(
    NotificationsDbContext db, ParallelReads parallel, ICurrentUser currentUser, NotificationPush push, IUnitOfWork unitOfWork, IClock clock)
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    private static Error NotFound => Error.NotFound("notification.not_found", "Njoftimi nuk u gjet.");

    /// <summary>GET /api/notifications: newest first, keyset (created_at DESC, id DESC) over ix_notif_user_list.</summary>
    public async Task<Result<KeysetPage<NotificationItem>>> ListAsync(bool unreadOnly, string? after, int? limit, CancellationToken ct)
    {
        DateTime? afterAt = null;
        ulong afterId = 0;
        if (after is not null)
        {
            if (Cursor.TryDecode(after, out var sortKey, out var id) && id > 0
                && long.TryParse(sortKey, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks > 0)
            {
                afterAt = new DateTime(ticks, DateTimeKind.Utc);
                afterId = (ulong)id;
            }
            else
            {
                return Error.ValidationFields("Kërkimi nuk është i vlefshëm.",
                    new Dictionary<string, string[]> { ["after"] = ["Kursori nuk vlen; filloni nga faqja e parë."] });
            }
        }

        var userId = (uint)currentUser.UserId;
        IQueryable<NotificationRow> Filtered(NotificationsDbContext context)
        {
            var rows = context.Notifications.AsNoTracking().Where(n => n.UserId == userId);
            return unreadOnly ? rows.Where(n => n.ReadAt == null) : rows;
        }

        var total = parallel.RunAsync<NotificationsDbContext, int>((other, token) => Filtered(other).CountAsync(token), ct);
        var page = Filtered(db);
        if (afterAt is { } at)
        {
            page = page.Where(n => n.CreatedAt < at || (n.CreatedAt == at && n.Id < afterId));
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await page.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(take + 1).ToListAsync(ct);
        var hasMore = rows.Count > take;
        rows = rows.Take(take).ToList();

        var items = rows.Select(n => new NotificationItem(n.PublicId, n.NotificationType, n.Title, n.Body, n.LinkPath,
                DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
                n.ReadAt is { } r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null))
            .ToList();
        var next = hasMore
            ? Cursor.Encode(rows[^1].CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture), (long)rows[^1].Id)
            : null;
        return new KeysetPage<NotificationItem>(items, next, await total);
    }

    /// <summary>GET /api/notifications/unread-count (ix_notif_user_unread).</summary>
    public async Task<UnreadCountResponse> UnreadCountAsync(CancellationToken ct) => new(await UnreadAsync(ct));

    /// <summary>POST /api/notifications/{id}/read: idempotent (an already read one keeps its first read_at).</summary>
    public async Task<Result<UnreadCountResponse>> MarkReadAsync(Guid publicId, CancellationToken ct)
    {
        var userId = (uint)currentUser.UserId;
        var row = await db.Notifications.SingleOrDefaultAsync(n => n.PublicId == publicId && n.UserId == userId, ct);
        if (row is null)
        {
            return NotFound;
        }

        if (row.ReadAt is null)
        {
            row.ReadAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            AfterCommitPushCount();
        }

        return new UnreadCountResponse(await UnreadAsync(ct));
    }

    /// <summary>POST /api/notifications/read-all: every unread notification of the current user.</summary>
    public async Task<ReadAllResponse> MarkAllReadAsync(CancellationToken ct)
    {
        var userId = (uint)currentUser.UserId;
        var now = clock.UtcNow;
        var marked = await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        if (marked > 0)
        {
            AfterCommitPushCount();
        }

        return new ReadAllResponse(marked, await UnreadAsync(ct));
    }

    private Task<int> UnreadAsync(CancellationToken ct)
    {
        var userId = (uint)currentUser.UserId;
        return db.Notifications.AsNoTracking().CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
    }

    private void AfterCommitPushCount()
    {
        var userId = currentUser.UserId;
        var publicId = currentUser.PublicId;
        unitOfWork.OnCommitted(() => push.CountChanged(userId, publicId));
    }
}
