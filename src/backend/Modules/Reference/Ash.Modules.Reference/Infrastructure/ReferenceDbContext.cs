using Ash.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ash.Modules.Reference.Infrastructure;

/// <summary>Read-only mapping of the lookup tables (task 014 adds editing).</summary>
public sealed class ReferenceDbContext(IUnitOfWork unitOfWork, IEnumerable<IInterceptor> interceptors) : ModuleDbContextBase(unitOfWork)
{
    private readonly IInterceptor[] _interceptors = interceptors.ToArray();

    public DbSet<CourtLevelRecord> CourtLevels => Set<CourtLevelRecord>();
    public DbSet<CourtRecord> Courts => Set<CourtRecord>();
    public DbSet<JudgeRecord> Judges => Set<JudgeRecord>();
    public DbSet<PartyRoleRecord> PartyRoles => Set<PartyRoleRecord>();
    public DbSet<SummonsTypeRecord> SummonsTypes => Set<SummonsTypeRecord>();
    public DbSet<HearingTypeRecord> HearingTypes => Set<HearingTypeRecord>();
    public DbSet<DocumentTypeRecord> DocumentTypes => Set<DocumentTypeRecord>();
    public DbSet<DecisionResultRecord> DecisionResults => Set<DecisionResultRecord>();
    public DbSet<CaseCategoryRecord> CaseCategories => Set<CaseCategoryRecord>();
    public DbSet<CaseSubcategoryRecord> CaseSubcategories => Set<CaseSubcategoryRecord>();
    public DbSet<AllowedFileTypeRecord> AllowedFileTypes => Set<AllowedFileTypeRecord>();
    public DbSet<DossierCodeFormatRecord> DossierCodeFormats => Set<DossierCodeFormatRecord>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        if (_interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(_interceptors);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CourtLevelRecord>(b =>
        {
            b.ToTable("court_levels");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.SortOrder).HasColumnName("sort_order");
            b.Property(x => x.AllowsDelegation).HasColumnName("allows_delegation");
            b.Property(x => x.AppealKind).HasColumnName("appeal_kind");
        });

        modelBuilder.Entity<CourtRecord>(b =>
        {
            b.ToTable("courts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.CourtLevelId).HasColumnName("court_level_id");
            b.Property(x => x.City).HasColumnName("city");
            b.Property(x => x.IsForeign).HasColumnName("is_foreign");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<JudgeRecord>(b =>
        {
            b.ToTable("judges");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.FirstName).HasColumnName("first_name");
            b.Property(x => x.LastName).HasColumnName("last_name");
            b.Property(x => x.CourtId).HasColumnName("court_id");
            b.Property(x => x.ExternalRef).HasColumnName("external_ref");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<PartyRoleRecord>(b =>
        {
            b.ToTable("party_roles");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.ReportGroup).HasColumnName("report_group");
            b.Property(x => x.SortOrder).HasColumnName("sort_order");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<SummonsTypeRecord>(b =>
        {
            b.ToTable("summons_types");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<HearingTypeRecord>(b =>
        {
            b.ToTable("hearing_types");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.IsOther).HasColumnName("is_other");
            b.Property(x => x.SortOrder).HasColumnName("sort_order");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<DocumentTypeRecord>(b =>
        {
            b.ToTable("document_types");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.IsOther).HasColumnName("is_other");
            b.Property(x => x.SortOrder).HasColumnName("sort_order");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<DecisionResultRecord>(b =>
        {
            b.ToTable("decision_results");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.OutcomeGroup).HasColumnName("outcome_group");
            b.Property(x => x.SendsBackForRetrial).HasColumnName("sends_back_for_retrial");
            b.Property(x => x.IsOther).HasColumnName("is_other");
            b.Property(x => x.SortOrder).HasColumnName("sort_order");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<CaseCategoryRecord>(b =>
        {
            b.ToTable("case_categories");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.IsDelegation).HasColumnName("is_delegation");
            b.Property(x => x.SortOrder).HasColumnName("sort_order");
        });

        modelBuilder.Entity<CaseSubcategoryRecord>(b =>
        {
            b.ToTable("case_subcategories");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.CategoryId).HasColumnName("category_id");
            b.Property(x => x.Code).HasColumnName("code");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.SortOrder).HasColumnName("sort_order");
        });

        modelBuilder.Entity<AllowedFileTypeRecord>(b =>
        {
            b.ToTable("allowed_file_types");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Extension).HasColumnName("extension");
            b.Property(x => x.ContentType).HasColumnName("content_type");
            b.Property(x => x.MagicHex).HasColumnName("magic_hex");
            b.Property(x => x.MaxSizeBytes).HasColumnName("max_size_bytes");
            b.Property(x => x.AppliesTo).HasColumnName("applies_to");
            b.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<DossierCodeFormatRecord>(b =>
        {
            b.ToTable("dossier_code_formats");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.Template).HasColumnName("template");
            b.Property(x => x.RandomDigits).HasColumnName("random_digits");
            b.Property(x => x.IsActive).HasColumnName("is_active");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.Note).HasColumnName("note");
        });
    }
}
