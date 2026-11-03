using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Documents.Infrastructure;

/// <summary>Documents module tables: documents (the file itself lives in object storage).</summary>
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
            b.Property(x => x.PublicId).HasColumnName("public_id").HasConversion<PublicIdConverter>();
            b.Property(x => x.DossierId).HasColumnName("dossier_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.DocumentTypeId).HasColumnName("document_type_id");
            b.Property(x => x.OtherTypeText).HasColumnName("other_type_text");
            b.Property(x => x.Title).HasColumnName("title");
            b.Property(x => x.OriginalFileName).HasColumnName("original_file_name");
            b.Property(x => x.ContentType).HasColumnName("content_type");
            b.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            b.Property(x => x.Sha256).HasColumnName("sha256");
            b.Property(x => x.StorageBucket).HasColumnName("storage_bucket");
            b.Property(x => x.StorageKey).HasColumnName("storage_key");
            b.Property(x => x.ScanStatus).HasColumnName("scan_status");
            b.Property(x => x.ScannedAt).HasColumnName("scanned_at");
            b.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
            b.Property(x => x.UploadedAt).HasColumnName("uploaded_at");
            b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            b.Property(x => x.DeletedBy).HasColumnName("deleted_by");
            b.Property(x => x.DeleteReason).HasColumnName("delete_reason");
        });
    }
}

public sealed class DocumentRow
{
    public const string Pending = "PENDING";
    public const string Clean = "CLEAN";
    public const string Infected = "INFECTED";
    public const string Failed = "FAILED";

    public ulong Id { get; set; }
    public Guid PublicId { get; set; }
    /// <summary>Documents belong to the dossier: one timeline for all levels.</summary>
    public ulong DossierId { get; set; }
    /// <summary>The level (X/n) the document was uploaded to; null = the dossier.</summary>
    public ulong? CaseId { get; set; }
    public byte DocumentTypeId { get; set; }
    public string? OtherTypeText { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>The user's file name, cleaned (no path, no control characters); never used in the storage key.</summary>
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public ulong SizeBytes { get; set; }
    public byte[] Sha256 { get; set; } = [];
    /// <summary>Quarantine until scanned CLEAN, then the documents bucket (same key).</summary>
    public string StorageBucket { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    /// <summary>PENDING → CLEAN | INFECTED | FAILED; downloadable only when CLEAN (task 034).</summary>
    public string ScanStatus { get; set; } = Pending;
    public DateTime? ScannedAt { get; set; }
    public uint UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
    /// <summary>Soft delete (task 034): the row and the stored object stay; the document leaves the timeline.</summary>
    public DateTime? DeletedAt { get; set; }
    /// <summary>Set together with DeletedAt (ck_documents_delete).</summary>
    public uint? DeletedBy { get; set; }
    public string? DeleteReason { get; set; }
}
