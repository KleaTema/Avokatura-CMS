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
        // read queries: MySQL stops them at the limit (the hint); EF's command timeout is the backstop (task 046)
        var limit = _unitOfWork.Options.QueryTimeLimit;
        optionsBuilder
            .UseMySQL(_unitOfWork.Connection, mysql => mysql.CommandTimeout((int)Math.Ceiling(limit.TotalSeconds) + 5))
            .AddInterceptors(new QueryTimeLimitInterceptor(limit));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<DateOnly>().HaveConversion<DateOnlyConverter>();
        configurationBuilder.Properties<DateOnly?>().HaveConversion<NullableDateOnlyConverter>();
    }
}
