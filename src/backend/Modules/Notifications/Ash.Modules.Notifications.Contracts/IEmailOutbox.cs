namespace Ash.Modules.Notifications.Contracts;

/// <summary>A plain-text e-mail to one address.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Queues e-mails in the caller's unit of work (table email_outbox, task 053c): they are written with the action and
/// sent only AFTER its commit by the dispatcher, with retries. A mail failure never fails the action; a rolled-back
/// action sends nothing.
/// </summary>
public interface IEmailOutbox
{
    Task EnqueueAsync(IReadOnlyCollection<EmailMessage> messages, CancellationToken ct = default);
}
