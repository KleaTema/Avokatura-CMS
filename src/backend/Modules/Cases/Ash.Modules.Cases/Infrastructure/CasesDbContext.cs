using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Infrastructure;

public sealed class CasesDbContext(IUnitOfWork unitOfWork) : ModuleDbContextBase(unitOfWork)
{
    public DbSet<Dossier> Dossiers => Set<Dossier>();
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseHearing> CaseHearings => Set<CaseHearing>();
    public DbSet<CaseParty> CaseParties => Set<CaseParty>();
    public DbSet<CaseInstitution> CaseInstitutions => Set<CaseInstitution>();
    public DbSet<CaseAccessRow> CaseAccess => Set<CaseAccessRow>();
    public DbSet<CasePermissionRow> CasePermissions => Set<CasePermissionRow>();
    public DbSet<CaseHistoryEvent> CaseHistoryEvents => Set<CaseHistoryEvent>();
    public DbSet<CaseAssignmentRow> CaseAssignments => Set<CaseAssignmentRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Rows are inserted by the create_dossier procedure (it issues the unique code); EF only reads them.
        modelBuilder.Entity<Dossier>(b =>
        {
            b.ToTable("dossiers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.UniqueCode).HasColumnName("unique_code");
            b.Property(x => x.OfficeId).HasColumnName("office_id");
            b.Property(x => x.OpenedBy).HasColumnName("opened_by");
            b.Property(x => x.OpenedAt).HasColumnName("opened_at");
            b.Property(x => x.ArchivedAt).HasColumnName("archived_at");
        });

        modelBuilder.Entity<Case>(b =>
        {
            b.ToTable("cases");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.DossierId).HasColumnName("dossier_id");
            b.Property(x => x.LevelSeq).HasColumnName("level_seq");
            b.Property(x => x.CaseCode).HasColumnName("case_code");
            b.Property(x => x.PreviousCaseId).HasColumnName("previous_case_id");
            b.Property(x => x.OfficeId).HasColumnName("office_id");
            b.Property(x => x.SummonsNumber).HasColumnName("summons_number");
            b.Property(x => x.SummonsTypeId).HasColumnName("summons_type_id");
            b.Property(x => x.ActNumber).HasColumnName("act_number");
            b.Property(x => x.ActDate).HasColumnName("act_date").HasColumnType("date");
            b.Property(x => x.ReceivedDate).HasColumnName("received_date").HasColumnType("date");
            b.Property(x => x.CourtLevelId).HasColumnName("court_level_id");
            b.Property(x => x.CourtId).HasColumnName("court_id");
            b.Property(x => x.JudgeId).HasColumnName("judge_id");
            b.Property(x => x.ObjectText).HasColumnName("object_text");
            b.Property(x => x.LifecycleStatus).HasColumnName("lifecycle_status");
            b.Property(x => x.NextHearingAt).HasColumnName("next_hearing_at");
            b.Property(x => x.RegisteredBy).HasColumnName("registered_by");
            b.Property(x => x.RegisteredAt).HasColumnName("registered_at");
            b.Property(x => x.ClosedAt).HasColumnName("closed_at");
            b.Property(x => x.RowVersion).HasColumnName("row_version");
        });

        modelBuilder.Entity<CaseHearing>(b =>
        {
            b.ToTable("case_hearings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.HearingTypeId).HasColumnName("hearing_type_id");
            b.Property(x => x.OtherTypeText).HasColumnName("other_type_text");
            b.Property(x => x.ScheduledAt).HasColumnName("scheduled_at");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.RowVersion).HasColumnName("row_version");
        });

        modelBuilder.Entity<CaseParty>(b =>
        {
            b.ToTable("case_parties");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.PartyRoleId).HasColumnName("party_role_id");
            b.Property(x => x.PartyKind).HasColumnName("party_kind");
            b.Property(x => x.InstitutionId).HasColumnName("institution_id");
            b.Property(x => x.DisplayName).HasColumnName("display_name");
            b.Property(x => x.Identifier).HasColumnName("identifier");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.Property(x => x.DeletedBy).HasColumnName("deleted_by");
        });

        modelBuilder.Entity<CaseInstitution>(b =>
        {
            b.ToTable("case_institutions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.InstitutionId).HasColumnName("institution_id");
            b.Property(x => x.LinkMode).HasColumnName("link_mode");
            b.Property(x => x.StartedAt).HasColumnName("started_at");
            b.Property(x => x.StartedBy).HasColumnName("started_by");
            b.Property(x => x.EndedAt).HasColumnName("ended_at");
            b.Property(x => x.EndedBy).HasColumnName("ended_by");
        });

        modelBuilder.Entity<CaseAssignmentRow>(b =>
        {
            b.ToTable("case_assignments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.CaseRole).HasColumnName("case_role");
            b.Property(x => x.EndedAt).HasColumnName("ended_at");
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

public sealed class Dossier
{
    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    public string UniqueCode { get; set; } = string.Empty;
    public ushort OfficeId { get; set; }
    public uint OpenedBy { get; set; }
    public DateTime OpenedAt { get; set; }
    /// <summary>Set when the dossier is closed with a final decision; an archived dossier is not continued.</summary>
    public DateTime? ArchivedAt { get; set; }
}

public sealed class Case
{
    public const string Registered = "REGISTERED";
    public const string Closed = "CLOSED";

    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    public ulong DossierId { get; set; }
    public ushort LevelSeq { get; set; }
    public string CaseCode { get; set; } = string.Empty;
    /// <summary>The closed case this one continues (appeal, recourse, retrial).</summary>
    public ulong? PreviousCaseId { get; set; }
    public ushort OfficeId { get; set; }
    public string SummonsNumber { get; set; } = string.Empty;
    public byte? SummonsTypeId { get; set; }
    public string ActNumber { get; set; } = string.Empty;
    /// <summary>Calendar date (DATE), never converted to UTC.</summary>
    public DateOnly? ActDate { get; set; }
    /// <summary>Calendar date (DATE), never converted to UTC.</summary>
    public DateOnly ReceivedDate { get; set; }
    public byte CourtLevelId { get; set; }
    public ushort CourtId { get; set; }
    public uint? JudgeId { get; set; }
    public string ObjectText { get; set; } = string.Empty;
    public string LifecycleStatus { get; set; } = string.Empty;
    public DateTime? NextHearingAt { get; set; }
    public uint RegisteredBy { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public uint RowVersion { get; set; }
}

public sealed class CaseHearing
{
    public const string Scheduled = "SCHEDULED";

    public ulong Id { get; set; }
    public ulong CaseId { get; set; }
    public byte HearingTypeId { get; set; }
    public string? OtherTypeText { get; set; }
    /// <summary>UTC.</summary>
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = Scheduled;
    public uint CreatedBy { get; set; }
    public uint RowVersion { get; set; }
}

public sealed class CaseParty
{
    public const string Natural = "NATURAL";
    public const string Legal = "LEGAL";
    public const string Institution = "INSTITUTION";

    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    public ulong CaseId { get; set; }
    public byte PartyRoleId { get; set; }
    public string PartyKind { get; set; } = string.Empty;
    /// <summary>Set exactly when PartyKind = INSTITUTION (CHECK constraint).</summary>
    public uint? InstitutionId { get; set; }
    /// <summary>For institutions a snapshot of the name at the time.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string? Identifier { get; set; }
    public uint CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Soft delete: a removed party stays for history.</summary>
    public DateTime? DeletedAt { get; set; }
    public uint? DeletedBy { get; set; }
}

public sealed class CaseInstitution
{
    public const string Monitoring = "MONITORING";
    public const string Delegated = "DELEGATED";

    public ulong Id { get; set; }
    public ulong CaseId { get; set; }
    public uint InstitutionId { get; set; }
    public string LinkMode { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public uint StartedBy { get; set; }
    /// <summary>Null = the link is active (one active link per case and institution, unique key).</summary>
    public DateTime? EndedAt { get; set; }
    public uint? EndedBy { get; set; }
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

public sealed class CaseAssignmentRow
{
    public const string PrimaryLawyer = "PRIMARY_LAWYER";

    public ulong Id { get; set; }
    public ulong CaseId { get; set; }
    public uint UserId { get; set; }
    public string CaseRole { get; set; } = string.Empty;
    public DateTime? EndedAt { get; set; }
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
