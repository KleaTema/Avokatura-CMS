using Ash.Modules.Identity.Application.Auth;

namespace Ash.Api.Configuration;

/// <summary>
/// Task 052, item 5 — outside Development the API checks its whole configuration at start and lists EVERY missing or
/// wrong required setting at once (not the first one, then the next restart, then the next…). Configuration comes only
/// from environment variables / mounted secrets: ASPNETCORE_* (the host), ConnectionStrings__*, the module sections
/// (Storage__*, Scanner__*, Email__*, Auth__Providers__*) and Ash__* (Ash__Instances, Ash__Redis__ConnectionString,
/// Ash__Version). appsettings.Production.json holds no secret (DeploymentTests checks it).
/// </summary>
public static class StartupValidator
{
    public static readonly string[] RequiredConnections = ["App", "Audit", "AuditDigest", "Chat", "Reporting"];

    public static IReadOnlyList<string> Problems(IConfiguration c, IHostEnvironment environment)
    {
        var problems = new List<string>();
        void Require(string key, string why)
        {
            if (string.IsNullOrWhiteSpace(c[key]))
            {
                problems.Add($"{key} is missing ({why}) — env {key.Replace(":", "__", StringComparison.Ordinal)}");
            }
        }

        foreach (var name in RequiredConnections)
        {
            Require($"ConnectionStrings:{name}", "database connection, SslMode=Required");
        }

        // object storage (task 032)
        Require("Storage:ServiceUrl", "S3 / MinIO endpoint");
        Require("Storage:AccessKey", "storage account");
        Require("Storage:SecretKey", "storage account, a mounted secret");
        foreach (var bucket in new[] { "Quarantine", "Documents", "Chat" })
        {
            Require($"Storage:Buckets:{bucket}", "bucket name");
        }

        // the virus scanner (task 033): ClamAV outside Development (NoOp is refused by DevFeatures)
        if (!string.Equals(c["Scanner:Mode"] ?? "ClamAv", "ClamAv", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Scanner:Mode must be ClamAv outside Development");
        }

        Require("Scanner:ClamAv:Host", "the clamd host");

        // e-mail (task 053c): when it is on, the relay must be known
        if (c.GetValue("Email:Enabled", true))
        {
            Require("Email:SmtpHost", "SMTP relay");
            Require("Email:FromAddress", "sender address");
            if (!string.IsNullOrWhiteSpace(c["Email:SmtpUser"]))
            {
                Require("Email:SmtpPassword", "SMTP account, a mounted secret");
            }
        }

        // identity (task 051)
        var providers = new AuthProvidersOptions { Providers = c.GetSection(AuthProvidersOptions.Section).Get<List<AuthProviderOptions>>() ?? [] };
        problems.AddRange(providers.Validate());

        // several instances: SignalR needs the Redis backplane (item 8)
        if (c.GetValue("Ash:Instances", 1) > 1)
        {
            Require("Ash:Redis:ConnectionString", "more than one API instance: the SignalR backplane");
        }

        return problems;
    }

    public static void Assert(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var problems = Problems(configuration, environment);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The configuration is incomplete ({problems.Count} problem(s)):{Environment.NewLine}  - " +
                string.Join(Environment.NewLine + "  - ", problems));
        }
    }
}
