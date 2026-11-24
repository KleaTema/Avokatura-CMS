using Ash.Infrastructure.Persistence;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySql.Data.MySqlClient;

namespace Ash.DemoSeed;

/// <summary>
/// The controllable clock of the demo seed: the application's own <see cref="IClock"/>, moved forward by the scenario
/// (registrations spread over Aug–Nov 2026, then "now" for this week's and next week's hearings).
/// </summary>
public sealed class DemoClock(DateTime startUtc) : IClock
{
    /// <summary>The start of the demo timeline: 1 August 2026, 07:00 UTC (09:00 in Tirane).</summary>
    public static readonly DateTime TimelineStart = new(2026, 8, 1, 7, 0, 0, DateTimeKind.Utc);

    public DemoClock() : this(TimelineStart)
    {
    }

    private DateTime _now = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);

    public DateTime UtcNow => _now;

    /// <summary>Moves to <paramref name="utc"/>; never backwards (history, audit and keyset orders follow time).</summary>
    public void Set(DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (utc > _now)
        {
            _now = utc;
        }
    }
}

/// <summary>The seed's changes to the API host — nothing of the application itself is replaced but the clock.</summary>
public static class DemoSeedServices
{
    public static void Configure(IServiceCollection services, DemoClock clock)
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(clock);

        // MySQL column defaults (CURRENT_TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP: case_hearings.created_at,
        // dossiers.opened_at, users.created_at, the updated_at columns …) follow the same clock: the unit of work's
        // connection sets the session TIMESTAMP when it opens. No raw INSERT/UPDATE, no change to the application.
        services.RemoveAll<MySqlUnitOfWork>();
        services.AddScoped(sp =>
        {
            var unit = new MySqlUnitOfWork(sp.GetRequiredService<PersistenceOptions>());
            unit.Connection.StateChange += (_, e) =>
            {
                if (e.CurrentState == System.Data.ConnectionState.Open)
                {
                    SetSessionTime(unit.Connection, clock.UtcNow);
                }
            };
            return unit;
        });
    }

    /// <summary>SET TIMESTAMP = the clock's Unix time (UTC; the server keeps time_zone = '+00:00').</summary>
    private static void SetSessionTime(MySqlConnection connection, DateTime utc)
    {
        var seconds = (utc - DateTime.UnixEpoch).TotalSeconds;
        using var command = new MySqlCommand("SET TIMESTAMP = @t", connection);
        command.Parameters.AddWithValue("@t", Math.Round(seconds, 3));
        command.ExecuteNonQuery();
    }
}
