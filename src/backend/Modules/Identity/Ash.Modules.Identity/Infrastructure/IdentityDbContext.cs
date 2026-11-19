using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(IUnitOfWork unitOfWork) : ModuleDbContextBase(unitOfWork)
{
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<UserIdentityRecord> UserIdentities => Set<UserIdentityRecord>();
    public DbSet<OfficeRecord> Offices => Set<OfficeRecord>();
    public DbSet<InstitutionRecord> Institutions => Set<InstitutionRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<UserRoleRecord> UserRoles => Set<UserRoleRecord>();
    public DbSet<OfficeCourtRecord> OfficeCourts => Set<OfficeCourtRecord>();
    public DbSet<OrgImportRecord> OrgImports => Set<OrgImportRecord>();
    public DbSet<UserOrgHistoryRecord> UserOrgHistory => Set<UserOrgHistoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // task 053g (script 025): the Organika imports and the per-user changes
        modelBuilder.Entity<OrgImportRecord>(b =>
        {
            b.ToTable("org_imports");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.FileName).HasColumnName("file_name");
            b.Property(x => x.ContentType).HasColumnName("content_type");
            b.Property(x => x.FileContent).HasColumnName("file_content");
            b.Property(x => x.Summary).HasColumnName("summary");
            b.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
            b.Property(x => x.UploadedAt).HasColumnName("uploaded_at");
            b.Property(x => x.AppliedBy).HasColumnName("applied_by");
            b.Property(x => x.AppliedAt).HasColumnName("applied_at");
            b.Property(x => x.AppliedCount).HasColumnName("applied_count");
            // task 053l: one table for every import kind, with the result report after "Apliko"
            b.Property(x => x.Kind).HasColumnName("kind");
            b.Property(x => x.Result).HasColumnName("result");
        });

        modelBuilder.Entity<UserOrgHistoryRecord>(b =>
        {
            b.ToTable("user_org_history");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.ImportId).HasColumnName("import_id");
            b.Property(x => x.OldPosition).HasColumnName("old_position");
            b.Property(x => x.NewPosition).HasColumnName("new_position");
            b.Property(x => x.OldOrgUnit).HasColumnName("old_org_unit");
            b.Property(x => x.NewOrgUnit).HasColumnName("new_org_unit");
            b.Property(x => x.ChangedBy).HasColumnName("changed_by");
            b.Property(x => x.ChangedAt).HasColumnName("changed_at");
        });

        // task 053c (script 021): the courts an office handles; at most one active office per court
        modelBuilder.Entity<OfficeCourtRecord>(b =>
        {
            b.ToTable("office_courts");
            b.HasKey(x => new { x.OfficeId, x.CourtId });
            b.Property(x => x.OfficeId).HasColumnName("office_id");
            b.Property(x => x.CourtId).HasColumnName("court_id");
            b.Property(x => x.IsActive).HasColumnName("is_active");
            b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        });

        // task 051 (script 028): the sign-in identities of a user, one per provider and subject
        modelBuilder.Entity<UserIdentityRecord>(b =>
        {
            b.ToTable("user_identities");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.ProviderName).HasColumnName("provider_name");
            b.Property(x => x.Subject).HasColumnName("subject");
            b.Property(x => x.EmailAtLink).HasColumnName("email_at_link");
            b.Property(x => x.LinkedAt).HasColumnName("linked_at");
            b.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            b.Property(x => x.BlockedAt).HasColumnName("blocked_at");
            b.Property(x => x.BlockedBy).HasColumnName("blocked_by");
        });

        modelBuilder.Entity<UserRecord>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            // users.external_subject is deprecated (task 051): replaced by user_identities, no longer read or written
            b.Property(x => x.Email).HasColumnName("email");
            b.Property(x => x.FirstName).HasColumnName("first_name");
            b.Property(x => x.LastName).HasColumnName("last_name");
            b.Property(x => x.Position).HasColumnName("position");
            b.Property(x => x.OrgUnit).HasColumnName("org_unit");
            b.Property(x => x.OfficeId).HasColumnName("office_id");
            b.Property(x => x.InstitutionId).HasColumnName("institution_id");
            b.Property(x => x.IsActive).HasColumnName("is_active");
            b.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            // set by MySQL on insert, never written by the app
            b.Property(x => x.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd()
                .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        });

        modelBuilder.Entity<RoleRecord>(b =>
        {
            b.ToTable("roles");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
        });

        modelBuilder.Entity<UserRoleRecord>(b =>
        {
            b.ToTable("user_roles");
            b.HasKey(x => new { x.UserId, x.RoleId });
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.RoleId).HasColumnName("role_id");
            b.Property(x => x.GrantedBy).HasColumnName("granted_by");
        });

        modelBuilder.Entity<OfficeRecord>(b =>
        {
            b.ToTable("offices");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.OfficeType).HasColumnName("office_type");
            b.Property(x => x.ParentOfficeId).HasColumnName("parent_office_id");
            b.Property(x => x.HeadUserId).HasColumnName("head_user_id");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<InstitutionRecord>(b =>
        {
            b.ToTable("institutions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.ShortName).HasColumnName("short_name");
            b.Property(x => x.InstitutionType).HasColumnName("institution_type");
            b.Property(x => x.ParentInstitutionId).HasColumnName("parent_institution_id");
            b.Property(x => x.Nipt).HasColumnName("nipt");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });
    }
}

/// <summary>A sign-in identity (task 051): the user at one provider, found by (ProviderName, Subject).</summary>
public sealed class UserIdentityRecord
{
    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    public uint UserId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? EmailAtLink { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    /// <summary>Task 051: "Shkëput" — this account may not sign in and is never re-linked by e-mail.</summary>
    public DateTime? BlockedAt { get; set; }
    public uint? BlockedBy { get; set; }
}

public sealed class UserRecord
{
    public uint Id { get; set; }
    public Guid PublicId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    /// <summary>Pozicioni, from the Organika import (task 053g).</summary>
    public string? Position { get; set; }
    /// <summary>The unit as the HR sheet names it (text, task 053g) — not the office, which decides access.</summary>
    public string? OrgUnit { get; set; }
    public ushort? OfficeId { get; set; }
    public uint? InstitutionId { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    /// <summary>users.created_at (the database's default; read-only here — task 053n "Krijuar këtë muaj").</summary>
    public DateTime CreatedAt { get; set; }
}

public sealed class RoleRecord
{
    public byte Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class UserRoleRecord
{
    public uint UserId { get; set; }
    public byte RoleId { get; set; }
    public uint? GrantedBy { get; set; }
}

public sealed class OfficeRecord
{
    public ushort Id { get; set; }
    public Guid PublicId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OfficeType { get; set; } = string.Empty;
    public ushort? ParentOfficeId { get; set; }
    public uint? HeadUserId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class OrgImportRecord
{
    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] FileContent { get; set; } = [];
    /// <summary>JSON of the preview counts.</summary>
    public string Summary { get; set; } = "{}";
    public uint UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
    public uint? AppliedBy { get; set; }
    public DateTime? AppliedAt { get; set; }
    public uint? AppliedCount { get; set; }

    /// <summary>Task 053l: ORGANIKA | USERS.</summary>
    public string Kind { get; set; } = "ORGANIKA";

    /// <summary>Task 053l: the rows after "Apliko" (JSON), the result report; null until applied.</summary>
    public string? Result { get; set; }
}

public sealed class UserOrgHistoryRecord
{
    public ulong Id { get; set; }
    public uint UserId { get; set; }
    public ulong ImportId { get; set; }
    public string? OldPosition { get; set; }
    public string? NewPosition { get; set; }
    public string? OldOrgUnit { get; set; }
    public string? NewOrgUnit { get; set; }
    public uint ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

public sealed class OfficeCourtRecord
{
    public ushort OfficeId { get; set; }
    public ushort CourtId { get; set; }
    public bool IsActive { get; set; }
    public uint UpdatedBy { get; set; }
}

public sealed class InstitutionRecord
{
    public uint Id { get; set; }
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string InstitutionType { get; set; } = string.Empty;
    public uint? ParentInstitutionId { get; set; }
    public string? Nipt { get; set; }
    public bool IsActive { get; set; } = true;
}
