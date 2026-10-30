using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Documents.Infrastructure;

/// <summary>Documents module tables. Only what is read so far; upload/download tasks extend the mapping.</summary>
public sealed class DocumentsDbContext(IUnitOfWork unitOfWork) : ModuleDbContextBase(unitOfWork)
{
    public DbSet<DocumentRow> Documents => Set<DocumentRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentRow>(b =>
        {
            b.ToTable("documents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        });
    }
}

public sealed class DocumentRow
{
    public ulong Id { get; set; }
    /// <summary>The level (X/n) the document was uploaded to; null = the dossier.</summary>
    public ulong? CaseId { get; set; }
    /// <summary>Soft delete.</summary>
    public DateTime? DeletedAt { get; set; }
}
