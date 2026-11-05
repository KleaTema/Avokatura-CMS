namespace Ash.Infrastructure.Persistence;

/// <param name="QueryTimeLimit">
/// Read queries (EF SELECTs) are stopped by MySQL after this long (QueryTimeLimitInterceptor). Configuration
/// "Database:QueryTimeLimitSeconds", default 5 s.
/// </param>
public sealed record PersistenceOptions(string ConnectionString, TimeSpan QueryTimeLimit)
{
    public static readonly TimeSpan DefaultQueryTimeLimit = TimeSpan.FromSeconds(5);

    public PersistenceOptions(string connectionString) : this(connectionString, DefaultQueryTimeLimit)
    {
    }
}
