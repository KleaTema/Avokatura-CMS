using Ash.Infrastructure.Persistence;
using Ash.Modules.Identity.Application;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Application.Institutions;
using Ash.Modules.Identity.Application.Offices;
using Ash.Modules.Identity.Application.Profile;
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
		services.AddScoped<ProfileHandlers>();
		// task 053l: the shared import pipeline and its kinds (Organika 053g, users)
		services.AddScoped<Application.Imports.ImportPipeline>();
		services.AddScoped<Application.Imports.OrganikaImport>();
		services.AddScoped<Application.Imports.UsersImport>();
		services.AddScoped<UserAdminSupport>();
		services.AddScoped<ListUsersHandler>();
		services.AddScoped<CreateUserHandler>();
		services.AddScoped<UpdateUserHandler>();
		services.AddScoped<SetUserActiveHandler>();
		services.AddScoped<GrantRoleHandler>();
		services.AddScoped<RevokeRoleHandler>();
		services.AddScoped<UserIdentityHandlers>(); // task 051
		services.AddScoped<ListRolesHandler>();
		services.AddScoped<ListOfficesAdminHandler>();
		services.AddScoped<CreateOfficeHandler>();
		services.AddScoped<UpdateOfficeHandler>();
		services.AddScoped<SetOfficeActiveHandler>();
		services.AddScoped<SetOfficeHeadHandler>();
		services.AddScoped<OfficeCourtsHandler>();
		services.AddScoped<ListInstitutionsAdminHandler>();
		services.AddScoped<CreateInstitutionHandler>();
		services.AddScoped<UpdateInstitutionHandler>();
		services.AddScoped<SetInstitutionActiveHandler>();
		services.AddScoped<SearchInstitutionsHandler>();
		services.AddScoped<IPermissionService, PermissionService>();
		services.AddScoped<IUserDirectory, UserDirectory>();
		services.AddScoped<IOfficeDirectory, OfficeDirectory>();
		services.AddScoped<IInstitutionDirectory, InstitutionDirectory>();
		services.AddScoped<CurrentUser>();
		// a ParallelReads child scope reads with the user of the request that started it
		services.AddScoped<ICurrentUser>(sp =>
			sp.GetRequiredService<InheritedCurrentUser>().User ?? sp.GetRequiredService<CurrentUser>());
		return services;
	}

	public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapMe().MapProfile().MapAdminUsers().MapAdminOrg().MapImports();
	}
}
