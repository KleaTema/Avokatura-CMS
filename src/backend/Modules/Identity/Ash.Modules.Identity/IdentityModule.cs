using Ash.Modules.Identity.Application;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Application.ResolveUser;
using Ash.Modules.Identity.Application.Users;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Identity.Endpoints;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Identity;

public static class IdentityModule
{
	public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddMemoryCache();
		services.AddScoped<IdentityDbContext>();
		services.AddScoped<ResolveUserHandler>();
		services.AddScoped<GetMeHandler>();
		services.AddScoped<UserAdminSupport>();
		services.AddScoped<ListUsersHandler>();
		services.AddScoped<CreateUserHandler>();
		services.AddScoped<UpdateUserHandler>();
		services.AddScoped<SetUserActiveHandler>();
		services.AddScoped<GrantRoleHandler>();
		services.AddScoped<RevokeRoleHandler>();
		services.AddScoped<IPermissionService, PermissionService>();
		services.AddScoped<IUserDirectory, UserDirectory>();
		services.AddScoped<CurrentUser>();
		services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
		return services;
	}

	public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapMe().MapAdminUsers();
	}
}
