using Ash.Modules.Notifications.Application.Email;
using Ash.Modules.Notifications.Contracts;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Ash.Modules.Notifications.Infrastructure;

/// <summary>
/// SMTP through MailKit (MIT), one connection per mail (a handful per action; the dispatcher sends one at a time).
/// TODO(akshi-q-smtp): AKSHI's relay — host, port, TLS mode, account and sender address.
/// </summary>
internal sealed class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        var security = options.SmtpSecurity switch
        {
            "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.None
        };
        await client.ConnectAsync(options.SmtpHost, options.SmtpPort, security, ct);
        if (!string.IsNullOrEmpty(options.SmtpUser))
        {
            await client.AuthenticateAsync(options.SmtpUser, options.SmtpPassword ?? string.Empty, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}
