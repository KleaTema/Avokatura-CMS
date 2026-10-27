using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Notifications;

public static class NotificationsModule
{
	public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
	{
		return services;
	}

	public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints;
	}
}
