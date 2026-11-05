using Ash.Modules.Notifications.Contracts;
using Ash.Modules.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Notifications.Application;

/// <summary><see cref="INotificationPreferences"/> over user_notification_prefs (task 048).</summary>
internal sealed class NotificationPreferences(NotificationsDbContext db) : INotificationPreferences
{
    public async Task<IReadOnlyList<NotificationPreference>> GetAsync(long userId, CancellationToken ct = default)
    {
        var id = (uint)userId;
        var off = await db.Prefs.AsNoTracking()
            .Where(p => p.UserId == id && !p.Enabled)
            .Select(p => p.NotificationType)
            .ToListAsync(ct);
        return NotificationTypes.Catalog
            .Select(t => new NotificationPreference(t.Type, t.Mandatory, t.Mandatory || !off.Contains(t.Type)))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> SetAsync(long userId, IReadOnlyDictionary<string, bool> choices, CancellationToken ct = default)
    {
        var refused = choices
            .Where(c => NotificationTypes.Find(c.Key) is not { } info || (info.Mandatory && !c.Value))
            .Select(c => c.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (refused.Count > 0)
        {
            return refused;
        }

        var id = (uint)userId;
        var optional = choices.Where(c => !NotificationTypes.Find(c.Key)!.Mandatory).ToList();
        var types = optional.Select(c => c.Key).ToList();
        var rows = await db.Prefs.Where(p => p.UserId == id && types.Contains(p.NotificationType)).ToListAsync(ct);
        foreach (var (type, enabled) in optional)
        {
            var row = rows.FirstOrDefault(r => r.NotificationType == type);
            if (enabled)
            {
                // on is the default: no row
                if (row is not null)
                {
                    db.Prefs.Remove(row);
                }
            }
            else if (row is null)
            {
                db.Prefs.Add(new NotificationPrefRow { UserId = id, NotificationType = type, Enabled = false });
            }
            else
            {
                row.Enabled = false;
            }
        }

        await db.SaveChangesAsync(ct);
        return [];
    }
}
