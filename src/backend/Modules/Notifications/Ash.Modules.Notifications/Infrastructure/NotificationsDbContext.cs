using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Notifications.Infrastructure;

/// <summary>Notifications module tables: notifications (004, public_id from 015), email_outbox (021).</summary>
public sealed class NotificationsDbContext(IUnitOfWork unitOfWork) : ModuleDbContextBase(unitOfWork)
{
    public DbSet<NotificationRow> Notifications => Set<NotificationRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationRow>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.NotificationType).HasColumnName("notification_type");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.Title).HasColumnName("title");
            b.Property(x => x.Body).HasColumnName("body");
            b.Property(x => x.LinkPath).HasColumnName("link_path");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.ReadAt).HasColumnName("read_at");
        });

        ConfigureEmail(modelBuilder);
    }

    public DbSet<EmailOutboxRow> EmailOutbox => Set<EmailOutboxRow>();

    /// <summary>Script 021 (task 053c).</summary>
    private static void ConfigureEmail(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EmailOutboxRow>(b =>
        {
            b.ToTable("email_outbox");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.ToAddress).HasColumnName("to_address");
            b.Property(x => x.Subject).HasColumnName("subject");
            b.Property(x => x.BodyText).HasColumnName("body_text");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.Attempts).HasColumnName("attempts");
            b.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            b.Property(x => x.LastError).HasColumnName("last_error");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.SentAt).HasColumnName("sent_at");
        });
}

/// <summary>An e-mail waiting to be sent, sent, or given up on (script 021).</summary>
public sealed class EmailOutboxRow
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Failed = "FAILED";

    public ulong Id { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string Status { get; set; } = Pending;
    public byte Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}

public sealed class NotificationRow
{
    public ulong Id { get; set; }
    /// <summary>Set by the application on insert (it is pushed before anything reads the row back).</summary>
    public Guid PublicId { get; set; }
    public uint UserId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public ulong? CaseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? LinkPath { get; set; }
    /// <summary>Set by the application (UTC, milliseconds) so the pushed and the stored value are the same.</summary>
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
