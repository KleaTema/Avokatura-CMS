using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Notifications.Infrastructure;

/// <summary>Notifications module table: notifications (004, public_id from 015).</summary>
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
    }
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
