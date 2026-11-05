using System.Text.RegularExpressions;
using Ash.Api.Auth;
using Serilog.Core;
using Serilog.Events;

namespace Ash.Api.Logging;

/// <summary>
/// Task 040: the SignalR hubs take the access token (production) or the dev user (Development) from the query
/// string, so a URL with its query string must never reach a log. Two layers: the host's "Request starting/finished"
/// lines (the only framework logs that write the query string) are turned off in Program, and this enricher masks the
/// value of those parameters in every string property of every event — whatever else might log a full URL.
/// Task 047: the search text "q" (/api/search, /api/cases, registration-search) is masked the same way.
/// </summary>
public sealed partial class LogRedaction : ILogEventEnricher
{
    public const string Mask = "***";

    /// <summary>The framework category that logs every request's full URL at Information.</summary>
    public const string HostingDiagnostics = "Microsoft.AspNetCore.Hosting.Diagnostics";

    // q: what people search for (names of parties, task 047) is personal data — never in a log either
    [GeneratedRegex($@"(?<=[?&](?:{HubQuery.AccessTokenName}|{DevAuthHandler.QueryName}|q)=)[^&#\s""']*", RegexOptions.IgnoreCase)]
    private static partial Regex Credentials();

    public static string Redact(string text) => Credentials().Replace(text, Mask);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<LogEventProperty>? replaced = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            if (value is ScalarValue { Value: string or Uri } scalar
                && scalar.Value.ToString() is { } text && Credentials().IsMatch(text))
            {
                (replaced ??= []).Add(new LogEventProperty(name, new ScalarValue(Redact(text))));
            }
        }

        foreach (var property in replaced ?? [])
        {
            logEvent.AddOrUpdateProperty(property);
        }
    }
}
