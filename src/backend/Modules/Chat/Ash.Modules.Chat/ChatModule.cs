using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Chat;

public static class ChatModule
{
	public static IServiceCollection AddChatModule(this IServiceCollection services, IConfiguration configuration)
	{
		return services;
	}

	public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints;
	}
}
