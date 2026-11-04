using Ash.Modules.Cases.Application;
using Ash.Modules.Cases.Application.Agenda;
using Ash.Modules.Cases.Application.ArchiveDossier;
using Ash.Modules.Cases.Application.AssignJurist;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.ChangeJudge;
using Ash.Modules.Cases.Application.ChangeObject;
using Ash.Modules.Cases.Application.DelegateCase;
using Ash.Modules.Cases.Application.Assistants;
using Ash.Modules.Cases.Application.FilterOptions;
using Ash.Modules.Cases.Application.SetCategory;
using Ash.Modules.Cases.Application.SetPriority;
using Ash.Modules.Cases.Application.SetSecurityMeasure;
using Ash.Modules.Cases.Application.SetSuspension;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Application.Hearings;
using Ash.Modules.Cases.Application.GetHistory;
using Ash.Modules.Cases.Application.ListCases;
using Ash.Modules.Cases.Application.Parties;
using Ash.Modules.Cases.Application.RecordDecision;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Application.UpdateDecision;
using Ash.Modules.Cases.Application.RegisterNextLevel;
using Ash.Modules.Cases.Application.RegistrationSearch;
using Ash.Modules.Cases.Application.TransferLawyer;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Endpoints;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.Modules.Cases.Infrastructure.History;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Cases;

public static class CasesModule
{
	public static IServiceCollection AddCasesModule(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddScoped<CasesDbContext>();
		services.AddScoped<CaseAccessService>();
		services.AddScoped<ICaseAssignmentCounts, CaseAssignmentCounts>();
		services.AddScoped<IHistoryWriter, HistoryWriter>();
		services.AddScoped<ICaseAccessService>(sp => sp.GetRequiredService<CaseAccessService>());
		services.AddScoped<PartyWriter>();
		services.AddScoped<PartyResolver>();
		services.AddScoped<PartyCaseGate>();
		services.AddScoped<PartyViews>();
		services.AddScoped<CaseRegistrationSupport>();
		services.AddScoped<RegisterCaseHandler>();
		services.AddScoped<NextLevelGate>();
		services.AddScoped<RegisterNextLevelHandler>();
		services.AddScoped<NextLevelPreviewHandler>();
		services.AddScoped<NotifyNotClosedHandler>();
		services.AddScoped<CaseListQueryBuilder>();
		services.AddScoped<ListCasesHandler>();
		services.AddScoped<CaseTabsHandler>();
		services.AddScoped<GetCaseHandler>();
		services.AddScoped<GetHistoryHandler>();
		services.AddScoped<RegistrationSearchHandler>();
		services.AddScoped<ListPartiesHandler>();
		services.AddScoped<AddPartyHandler>();
		services.AddScoped<UpdatePartyHandler>();
		services.AddScoped<RemovePartyHandler>();
		services.AddScoped<OfficeLawyers>();
		services.AddScoped<AssignableLawyersHandler>();
		services.AddScoped<FilterOptionsHandler>();
		services.AddScoped<AssignLawyersHandler>();
		services.AddScoped<TransferLawyerHandler>();
		services.AddScoped<FirstDataEntry>();
		services.AddScoped<ICaseActivity, CaseActivity>();
		services.AddScoped<CaseDataGate>();
		services.AddScoped<UpdateClaimHandler>();
		services.AddScoped<SetCategoryHandler>();
		services.AddScoped<SetSuspensionHandler>();
		services.AddScoped<SetSecurityMeasureHandler>();
		services.AddScoped<SetPriorityHandler>();
		services.AddScoped<AssignableAssistantsHandler>();
		services.AddScoped<AddAssistantHandler>();
		services.AddScoped<RemoveAssistantHandler>();
		services.AddScoped<InstitutionJurists>();
		services.AddScoped<AssignableJuristsHandler>();
		services.AddScoped<AssignJuristHandler>();
		services.AddScoped<DelegateCaseHandler>();
		services.AddScoped<UndelegateCaseHandler>();
		services.AddScoped<ChangeJudgeHandler>();
		services.AddScoped<ChangeObjectHandler>();
		services.AddScoped<HearingWriter>();
		services.AddScoped<ListHearingsHandler>();
		services.AddScoped<NewHearingDefaultsHandler>();
		services.AddScoped<AddHearingHandler>();
		services.AddScoped<UpdateHearingHandler>();
		services.AddScoped<CompleteHearingHandler>();
		services.AddScoped<DecisionSupport>();
		services.AddScoped<RecordDecisionHandler>();
		services.AddScoped<UpdateDecisionHandler>();
		services.AddScoped<ArchiveDossierHandler>();
		services.AddScoped<AgendaQueryBuilder>();
		services.AddScoped<GetAgendaHandler>();
		return services;
	}

	public static IEndpointRouteBuilder MapCasesEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapCaseRegistration().MapCaseParties().MapCaseList().MapCaseDetail().MapCaseAssignment()
			.MapCaseData().MapCaseInstitutions().MapCaseHearings().MapAgenda().MapCaseDecision().MapDossiers();
	}
}
