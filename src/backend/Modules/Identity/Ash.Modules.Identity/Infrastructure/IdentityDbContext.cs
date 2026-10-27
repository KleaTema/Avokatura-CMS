using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(IUnitOfWork unitOfWork) : ModuleDbContextBase(unitOfWork)
{
    public DbSet<UserRecord> Users => Set<UserRecord>();

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
            b.Property(x => x.OfficeId).HasColumnName("office_id");
            b.Property(x => x.InstitutionId).HasColumnName("institution_id");
            b.Property(x => x.IsActive).HasColumnName("is_active");
            b.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
        });
    }
}

public sealed class UserRecord
{
    public uint Id { get; set; }
    public Guid PublicId { get; set; }
    public string ExternalSubject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public ushort? OfficeId { get; set; }
    public uint? InstitutionId { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
