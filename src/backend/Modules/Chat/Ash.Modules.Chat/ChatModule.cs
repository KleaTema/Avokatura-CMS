using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Chat.Application;
using Ash.Modules.Chat.Application.Policy;
using Ash.Modules.Chat.Endpoints;
using Ash.Modules.Chat.Infrastructure;
using Ash.SharedKernel.Events;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Chat;

public static class ChatModule
{
	public static IServiceCollection AddChatModule(this IServiceCollection services, IConfiguration configuration)
	{
		// task 049: the chat has its own account and its own transaction (ConnectionStrings:Chat), never the core one
		var connectionString = configuration.GetConnectionString("Chat")
			?? throw new InvalidOperationException(
				"ConnectionStrings:Chat is missing. Set it with: dotnet user-secrets set \"ConnectionStrings:Chat\" \"<value>\" " +
				"(locally: Server=localhost;Port=3306;User=ash_chat_local;Password=ash_chat_local_pw;Database=ash_chat;)");
		services.AddSingleton(sp => new ChatOptions(
			sp.GetRequiredService<PersistenceOptions>() with { ConnectionString = connectionString }));
		services.AddScoped<ChatUnitOfWork>();
		services.AddScoped<ChatDbContext>();

		// Chat:Policy — CaseOnly (the client's decision, default) or Open. TODO(client-q-chat)
		var policy = configuration[ChatPolicies.Section] ?? ChatPolicies.CaseOnly;
		if (policy == ChatPolicies.Open)
		{
			services.AddScoped<IChatPolicy, OpenChatPolicy>();
		}
		else if (policy == ChatPolicies.CaseOnly)
		{
			services.AddScoped<IChatPolicy, CaseOnlyChatPolicy>();
		}
		else
		{
			throw new InvalidOperationException($"Chat:Policy '{policy}' is unknown: use {ChatPolicies.CaseOnly} or {ChatPolicies.Open}.");
		}

		services.AddSingleton<ChatConnections>();
		services.AddSingleton<ChatPush>();
		services.AddSingleton<ChatRateLimiter>();
		services.AddScoped<MessageStore>();
		services.AddScoped<ChatParticipants>();
		services.AddScoped<ChatService>();

		services.AddScoped<ChatCaseEvents>();
		services.AddScoped<IEventHandler<LawyerAssigned>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<LawyerTransferred>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<AssistantAssigned>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<AssistantRemoved>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<JuristAssigned>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<InstitutionLinked>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<InstitutionUnlinked>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<CaseDelegated>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<CaseUndelegated>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScoped<IEventHandler<DossierArchived>>(sp => sp.GetRequiredService<ChatCaseEvents>());
		services.AddScheduledJob<ChatReconcileJob>();
		return services;
	}

	public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapChat();
	}
}
