using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Ash.Api.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddAshAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var builder = services.AddAuthentication();

        if (environment.IsDevelopment())
        {
            services.Configure<AuthenticationOptions>(o => o.DefaultScheme = DevAuthHandler.SchemeName);
            builder.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, null);
        }
        else
        {
            var authority = configuration["Auth:Authority"];
            var audience = configuration["Auth:Audience"];
            if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
            {
                throw new InvalidOperationException(
                    "Auth:Authority and Auth:Audience must be configured outside Development.");
            }

            services.Configure<AuthenticationOptions>(o => o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme);
            builder.AddJwtBearer(o =>
            {
                o.Authority = authority;
                o.Audience = audience;
                o.MapInboundClaims = false;
            });
        }

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build());

        return services;
    }
}
