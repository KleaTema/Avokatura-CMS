using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(IUnitOfWork unitOfWork) : ModuleDbContextBase(unitOfWork)
{
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<OfficeRecord> Offices => Set<OfficeRecord>();
    public DbSet<InstitutionRecord> Institutions => Set<InstitutionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRecord>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
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

        modelBuilder.Entity<OfficeRecord>(b =>
        {
            b.ToTable("offices");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.Name).HasColumnName("name");
        });

        modelBuilder.Entity<InstitutionRecord>(b =>
        {
            b.ToTable("institutions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.Name).HasColumnName("name");
        });
    }
}

public sealed class UserRecord
{
    public uint Id { get; set; }
    public Guid PublicId { get; set; }
    public string ExternalSubject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public ushort? OfficeId { get; set; }
    public uint? InstitutionId { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public sealed class OfficeRecord
{
    public ushort Id { get; set; }
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class InstitutionRecord
{
    public uint Id { get; set; }
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
}
