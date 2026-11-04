using System.Globalization;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Notifications.Application;

/// <summary>
/// Every notification text in one place (Albanian). A snapshot, written when the notification is stored: only the case
/// code, the court, the action and people's names — never the object of the case or any other case content (task 040).
/// Links are app routes with public ids only: /cases/{publicId}/{tab}, tabs as in the frontend's CasePage.
/// </summary>
public static class NotificationTexts
{
    /// <summary>The case page tabs a notification links to (src/frontend/src/pages/cases/CasePage.tsx CASE_TABS).</summary>
    public static class Tabs
    {
        public const string Data = "te-dhenat";
        public const string Documents = "dokumentet";
        public const string Decision = "vendimi";
    }

    public const string PrimaryRole = "avokat kryesor";
    public const string SecondaryRole = "avokat dytësor";

    public static string LinkTo(Guid casePublicId, string tab) => $"/cases/{casePublicId}/{tab}";

    private static string Case(CaseSummary c) =>
        string.IsNullOrEmpty(c.CourtName) ? c.CaseCode : $"{c.CaseCode} ({c.CourtName})";

    private static NotificationMessage Message(string type, CaseSummary c, string title, string body, string tab) =>
        new(type, c.Id, title, body, LinkTo(c.PublicId, tab));

    public static NotificationMessage CaseRegistered(CaseSummary c) =>
        Message(NotificationTypes.CaseRegistered, c, "Çështje e re për caktim",
            $"Çështja {Case(c)} u regjistrua dhe pret caktimin e avokatit.", Tabs.Data);

    public static NotificationMessage LawyerAssigned(CaseSummary c, string role) =>
        Message(NotificationTypes.LawyerAssigned, c, "Ju u caktua një çështje",
            $"U caktuat {role} në çështjen {Case(c)}.", Tabs.Data);

    public static NotificationMessage LawyerRemoved(CaseSummary c, string role, string? newLawyer) =>
        Message(NotificationTypes.LawyerRemoved, c, "Çështja u transferua",
            newLawyer is null
                ? $"Nuk jeni më {role} në çështjen {Case(c)}."
                : $"Nuk jeni më {role} në çështjen {Case(c)}; u caktua {newLawyer}.", Tabs.Data);

    public static NotificationMessage InstitutionCaseAssigned(CaseSummary c, string primaryLawyer) =>
        Message(NotificationTypes.InstitutionCaseAssigned, c, "Çështjes iu caktua avokati",
            $"Çështja {Case(c)} iu caktua avokatit {primaryLawyer}.", Tabs.Data);

    public static NotificationMessage AssistantAssigned(CaseSummary c) =>
        Message(NotificationTypes.AssistantAssigned, c, "U shtuat si ndihmës ligjor",
            $"U shtuat ndihmës ligjor në çështjen {Case(c)}.", Tabs.Data);

    public static NotificationMessage AssistantRemoved(CaseSummary c) =>
        Message(NotificationTypes.AssistantRemoved, c, "U hoqët si ndihmës ligjor",
            $"Nuk jeni më ndihmës ligjor në çështjen {Case(c)}.", Tabs.Data);

    public static NotificationMessage JuristAssigned(CaseSummary c, string institution) =>
        Message(NotificationTypes.JuristAssigned, c, "U caktuat jurist",
            $"{institution} ju caktoi jurist në çështjen {Case(c)}.", Tabs.Data);

    public static NotificationMessage JuristReplaced(CaseSummary c, string institution, string newJurist) =>
        Message(NotificationTypes.JuristReplaced, c, "U zëvendësuat si jurist",
            $"{institution} caktoi juristin {newJurist} në vendin tuaj në çështjen {Case(c)}.", Tabs.Data);

    public static NotificationMessage InstitutionLinked(CaseSummary c, string institution, bool delegated) =>
        Message(NotificationTypes.InstitutionLinked, c, "Çështje e re për institucionin",
            delegated
                ? $"{institution} u lidh me çështjen {Case(c)} me delegim."
                : $"{institution} u lidh me çështjen {Case(c)} për monitorim.", Tabs.Data);

    public static NotificationMessage CaseDelegated(CaseSummary c, string institution, bool related) =>
        Message(NotificationTypes.CaseDelegated, c, related ? "Çështja u relatua" : "Çështja u delegua",
            related
                ? $"Çështja {Case(c)} iu relatua institucionit {institution}."
                : $"Çështja {Case(c)} iu delegua institucionit {institution}.", Tabs.Data);

    public static NotificationMessage CaseUndelegated(CaseSummary c, string institution) =>
        Message(NotificationTypes.CaseUndelegated, c, "Delegimi përfundoi",
            $"Delegimi i çështjes {Case(c)} te {institution} përfundoi; institucioni e monitoron përsëri.", Tabs.Data);

    /// <summary>HearingAt is UTC; the text shows it in Tirane time.</summary>
    public static NotificationMessage PreviousLevelNotClosed(CaseSummary c, string summonsNumber, DateTime hearingAtUtc) =>
        Message(NotificationTypes.PreviousLevelNotClosed, c, "Shkalla nuk është mbyllur",
            $"Erdhi thirrja {summonsNumber} për shkallën tjetër, me seancë më {TiraneDateTime(hearingAtUtc)}, " +
            $"por çështja {Case(c)} nuk është mbyllur në sistem. Regjistroni vendimin.", Tabs.Decision);

    public static NotificationMessage CaseClosed(CaseSummary c, string? closedBy) =>
        Message(NotificationTypes.CaseClosed, c, "Çështja u mbyll",
            closedBy is null
                ? $"Çështja {Case(c)} u mbyll me regjistrimin e vendimit."
                : $"{closedBy} mbylli çështjen {Case(c)} me regjistrimin e vendimit.", Tabs.Decision);

    public static NotificationMessage DocumentInfected(CaseSummary c) =>
        Message(NotificationTypes.DocumentInfected, c, "Dokumenti u refuzua",
            $"Një dokument i ngarkuar në çështjen {Case(c)} u refuzua nga kontrolli i sigurisë.", Tabs.Documents);

    public static NotificationMessage DocumentScanFailed(CaseSummary c) =>
        Message(NotificationTypes.DocumentScanFailed, c, "Dokumenti nuk u kontrollua",
            $"Një dokument i ngarkuar në çështjen {Case(c)} nuk mund të kontrollohej për viruse.", Tabs.Documents);

    private static string TiraneDateTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TiraneTime.Zone)
            .ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}
