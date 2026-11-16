using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Ash.Api.Security;

/// <summary>
/// Task 052, item 4 — the API behind a reverse proxy (the frontend's nginx or AKSHI's ingress). X-Forwarded-For /
/// X-Forwarded-Proto are honoured ONLY from the configured proxies (ReverseProxy:KnownProxies — IP addresses,
/// ReverseProxy:KnownNetworks — CIDR, e.g. 10.20.0.0/16); from anyone else they are ignored. The client's real address
/// is what the audit log records and what anonymous rate limits count by; the scheme decides HSTS and redirect URIs.
/// TODO(akshi-q-hosting): the proxy / ingress addresses of the AKSHI environments.
/// </summary>
public static class ReverseProxy
{
    public static IServiceCollection AddAshForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var networks = configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // nothing is trusted by default (the framework would trust loopback): only what is configured
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            o.ForwardLimit = 1;
            foreach (var proxy in proxies.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                o.KnownProxies.Add(IPAddress.Parse(proxy.Trim()));
            }

            foreach (var network in networks.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network.Trim()));
            }
        });
        return services;
    }
}
