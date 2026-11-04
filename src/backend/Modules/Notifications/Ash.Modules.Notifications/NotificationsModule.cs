using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Documents.Contracts.Events;
using Ash.Modules.Notifications.Application;
using Ash.Modules.Notifications.Contracts;
using Ash.Modules.Notifications.Endpoints;
using Ash.Modules.Notifications.Infrastructure;
using Ash.Modules.Notifications.Realtime;
using Ash.SharedKernel.Events;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Notifications;

public static class NotificationsModule
{
	public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSignalR();
		services.AddSingleton<IUserIdProvider, PublicIdUserIdProvider>();
		services.AddSingleton<NotificationPush>();

		services.AddScoped<NotificationsDbContext>();
		services.AddScoped<CurrentActor>();
		services.AddScoped<INotificationSender, NotificationSender>();
		services.AddScoped<NotificationQueries>();

		services.AddScoped<CaseEventNotifications>();
		services.AddScoped<IEventHandler<CaseRegistered>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<LawyerAssigned>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<LawyerTransferred>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<AssistantAssigned>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<AssistantRemoved>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<JuristAssigned>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<InstitutionLinked>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<CaseDelegated>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<CaseUndelegated>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<PreviousLevelNotClosed>>(sp => sp.GetRequiredService<CaseEventNotifications>());
		services.AddScoped<IEventHandler<CaseClosed>>(sp => sp.GetRequiredService<CaseEventNotifications>());

		services.AddScoped<DocumentEventNotifications>();
		services.AddScoped<IEventHandler<DocumentInfected>>(sp => sp.GetRequiredService<DocumentEventNotifications>());
		services.AddScoped<IEventHandler<DocumentScanFailed>>(sp => sp.GetRequiredService<DocumentEventNotifications>());
		services.AddScoped<IEventHandler<DocumentScanned>>(sp => sp.GetRequiredService<DocumentEventNotifications>());
		return services;
	}

	public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapNotifications();
	}
}
