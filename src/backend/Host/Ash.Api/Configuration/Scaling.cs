namespace Ash.Api.Configuration;

/// <summary>
/// Task 052, item 8 — several API instances behind the proxy:
/// <list type="bullet">
/// <item>SignalR (notifications task 040, chat task 049): with Ash__Redis__ConnectionString every instance publishes
/// through Redis, so a push reaches a user whichever instance holds their WebSocket — no sticky sessions needed;</item>
/// <item>Ash__Instances &gt; 1 without Redis → the app refuses to start (StartupValidator), a push would reach only the
/// users of the instance that sent it;</item>
/// <item>background jobs already run once across instances: each run takes a MySQL GET_LOCK (task 041);</item>
/// <item>the in-memory caches (roles 60 s, dashboard, statistics) stay per instance — the immediate-effect rules of
/// task 051 remove the entry on the instance that made the change; other instances follow within the cache time.</item>
/// </list>
/// </summary>
public static class Scaling
{
    public const string ChannelPrefix = "ash";

    public static IServiceCollection AddAshSignalRBackplane(this IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration["Ash:Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddSignalR().AddStackExchangeRedis(redis, o =>
                o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal(ChannelPrefix));
        }

        return services;
    }
}
