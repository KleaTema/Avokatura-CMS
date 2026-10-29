using Microsoft.EntityFrameworkCore;

namespace Ash.Infrastructure.Persistence;

/// <summary>Base for every module DbContext: shares the request's connection and transaction.</summary>
public abstract class ModuleDbContextBase : DbContext
{
    private readonly IUnitOfWork _unitOfWork;

    protected ModuleDbContextBase(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
        unitOfWork.Enlist(this);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseMySQL(_unitOfWork.Connection);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<DateOnly>().HaveConversion<DateOnlyConverter>();
        configurationBuilder.Properties<DateOnly?>().HaveConversion<NullableDateOnlyConverter>();
    }
}
