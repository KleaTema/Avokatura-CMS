using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(IUnitOfWork unitOfWork) : ModuleDbContextBase(unitOfWork)
{
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<OfficeRecord> Offices => Set<OfficeRecord>();
    public DbSet<InstitutionRecord> Institutions => Set<InstitutionRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<UserRoleRecord> UserRoles => Set<UserRoleRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRecord>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.ExternalSubject).HasColumnName("external_subject");
            b.Property(x => x.Email).HasColumnName("email");
            b.Property(x => x.FirstName).HasColumnName("first_name");
            b.Property(x => x.LastName).HasColumnName("last_name");
            b.Property(x => x.OfficeId).HasColumnName("office_id");
            b.Property(x => x.InstitutionId).HasColumnName("institution_id");
            b.Property(x => x.IsActive).HasColumnName("is_active");
            b.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
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

public sealed class UserRecord
{
    public uint Id { get; set; }
    public Guid PublicId { get; set; }
    public string? ExternalSubject { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public ushort? OfficeId { get; set; }
    public uint? InstitutionId { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
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
