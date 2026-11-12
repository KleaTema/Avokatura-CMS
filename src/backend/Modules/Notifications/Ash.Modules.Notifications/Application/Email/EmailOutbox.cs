using Ash.Infrastructure.Persistence;
using Ash.Modules.Notifications.Contracts;
using Ash.Modules.Notifications.Infrastructure;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Notifications.Application.Email;

/// <summary>
/// <see cref="IEmailOutbox"/>: PENDING rows in the caller's transaction; after the commit the dispatcher is woken so
/// the mails leave within seconds (its timer is the fallback, and the retry).
/// </summary>
internal sealed class EmailOutbox(NotificationsDbContext db, IUnitOfWork unitOfWork, EmailDispatcher dispatcher, IClock clock)
    : IEmailOutbox
{
    public const int MaxSubject = 300;
    public const int MaxAddress = 254;

    public async Task EnqueueAsync(IReadOnlyCollection<EmailMessage> messages, CancellationToken ct = default)
    {
        var rows = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.To) && m.To.Length <= MaxAddress)
            .Select(m => new EmailOutboxRow
            {
                ToAddress = m.To.Trim(),
                Subject = m.Subject.Length <= MaxSubject ? m.Subject : m.Subject[..MaxSubject],
                BodyText = m.Body,
                Status = EmailOutboxRow.Pending,
                NextAttemptAt = clock.UtcNow,
                CreatedAt = clock.UtcNow,
            })
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }

        db.EmailOutbox.AddRange(rows);
        await db.SaveChangesAsync(ct);
        unitOfWork.OnCommitted(dispatcher.Wake);
    }
}
