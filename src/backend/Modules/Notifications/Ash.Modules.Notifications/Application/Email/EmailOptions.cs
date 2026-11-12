namespace Ash.Modules.Notifications.Application.Email;

/// <summary>
/// Configuration section "Email" (task 053c). Locally Mailpit (docker-compose: SMTP :1025, web UI :8025), in
/// production AKSHI's relay. TODO(akshi-q-smtp): host, port, TLS mode, account and sender address of AKSHI's relay.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>The dispatcher sends; off in the integration tests (they run it by hand with a fake sender).</summary>
    public bool Enabled { get; set; } = true;

    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 1025;

    /// <summary>None (Mailpit), StartTls or SslOnConnect.</summary>
    public string SmtpSecurity { get; set; } = "None";

    /// <summary>Empty: no authentication.</summary>
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }

    public string FromAddress { get; set; } = "noreply@ash.local";
    public string FromName { get; set; } = "Avokatura e Shtetit";

    /// <summary>How often the dispatcher looks for due mails (it is also woken after each commit that queued one).</summary>
    public int IntervalSeconds { get; set; } = 30;

    public int BatchSize { get; set; } = 20;

    /// <summary>After this many failed sends the mail is FAILED (kept, with its last error).</summary>
    public int MaxAttempts { get; set; } = 8;
}
