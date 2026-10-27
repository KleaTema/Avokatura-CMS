using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Infrastructure;

public sealed class CasesDbContext(IUnitOfWork unitOfWork) : ModuleDbContextBase(unitOfWork)
{
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseAccessRow> CaseAccess => Set<CaseAccessRow>();
    public DbSet<CasePermissionRow> CasePermissions => Set<CasePermissionRow>();
    public DbSet<CaseHistoryEvent> CaseHistoryEvents => Set<CaseHistoryEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Case>(b =>
        {
            b.ToTable("cases");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.DossierId).HasColumnName("dossier_id");
            b.Property(x => x.OfficeId).HasColumnName("office_id");
            b.Property(x => x.LifecycleStatus).HasColumnName("lifecycle_status");
            b.Property(x => x.RegisteredBy).HasColumnName("registered_by");
        });

        modelBuilder.Entity<CaseAccessRow>(b =>
        {
            b.HasNoKey();
            b.ToView("v_case_access");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.AccessProfile).HasColumnName("access_profile");
        });

        modelBuilder.Entity<CasePermissionRow>(b =>
        {
            b.HasNoKey();
            b.ToView("v_case_permissions");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.AccessProfile).HasColumnName("access_profile");
            b.Property(x => x.PermissionCode).HasColumnName("permission_code");
        });
            modelBuilder.Entity<CaseHistoryEvent>(b =>
            {
                b.ToTable("case_history_events");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasColumnName("id");
                b.Property(x => x.DossierId).HasColumnName("dossier_id");
                b.Property(x => x.CaseId).HasColumnName("case_id");
                b.Property(x => x.EventType).HasColumnName("event_type");
                b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
                b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
                b.Property(x => x.Summary).HasColumnName("summary");
                b.Property(x => x.Details).HasColumnName("details").HasColumnType("json");
            });
        }
    }

    public sealed class CaseHistoryEvent
    {
        public ulong Id { get; set; }
        public ulong DossierId { get; set; }
        public ulong? CaseId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public uint? ActorUserId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string? Details { get; set; }
    }

    public sealed class Case
    {
    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    public ulong DossierId { get; set; }
    public ushort OfficeId { get; set; }
    public string LifecycleStatus { get; set; } = string.Empty;
    public uint RegisteredBy { get; set; }
}

public sealed class CaseAccessRow
{
    public uint UserId { get; set; }
    public ulong CaseId { get; set; }
    public string AccessProfile { get; set; } = string.Empty;
}

public sealed class CasePermissionRow
{
    public uint UserId { get; set; }
    public ulong CaseId { get; set; }
    public string AccessProfile { get; set; } = string.Empty;
    public string PermissionCode { get; set; } = string.Empty;
}
