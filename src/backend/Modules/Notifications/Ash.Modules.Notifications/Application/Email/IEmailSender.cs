using Ash.Modules.Notifications.Contracts;

namespace Ash.Modules.Notifications.Application.Email;

/// <summary>Sends one e-mail now (SMTP: <see cref="Infrastructure.SmtpEmailSender"/>). Throws when it was not sent.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
