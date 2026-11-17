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
        public const string Hearings = "seancat";
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

    /// <summary>Task 053e: the assistant's "Çështjet e mia" now holds the lawyer's cases.</summary>
    public const string MyCasesLink = "/cases?tab=mine";

    public static NotificationMessage AssistantLinked(string lawyer, int cases) =>
        new(NotificationTypes.AssistantAssigned, null, "U lidhët me një avokat si ndihmës ligjor",
            $"Kryetari ju lidhi si ndihmës ligjor me avokatin {lawyer}: shihni çështjet ku ai është avokat primar " +
            $"({cases} tani, edhe ato që do t'i caktohen më vonë).", MyCasesLink);

    public static NotificationMessage AssistantUnlinked(string lawyer) =>
        new(NotificationTypes.AssistantRemoved, null, "U hoq lidhja me avokatin",
            $"Kryetari hoqi lidhjen tuaj si ndihmës ligjor me avokatin {lawyer}: çështjet e tij nuk i shihni më.", MyCasesLink);

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

    /// <summary>Task 053d: the Kryetar's "Në mbikëqyrje" tab of the case list.</summary>
    public const string SupervisedLink = "/cases?tab=supervised";

    /// <summary>A lawyer's cases (<paramref name="lawyer"/>) or one case (<paramref name="c"/>) under the Kryetar's supervision.</summary>
    public static NotificationMessage SupervisionAssigned(string? lawyer, CaseSummary? c) =>
        new(NotificationTypes.SupervisionAssigned, c?.Id, "Ju u ngarkua një mbikëqyrje",
            c is not null
                ? $"Monitoruesi ju ngarkoi në mbikëqyrje çështjen {Case(c)}. E gjeni te \"Në mbikëqyrje\"."
                : $"Monitoruesi ju ngarkoi në mbikëqyrje çështjet e avokatit {lawyer} (si avokat primar). I gjeni te \"Në mbikëqyrje\".",
            c is not null ? LinkTo(c.PublicId, Tabs.Data) : SupervisedLink);

    public static NotificationMessage SupervisionEnded(string? lawyer, CaseSummary? c) =>
        new(NotificationTypes.SupervisionEnded, c?.Id, "Mbikëqyrja përfundoi",
            c is not null
                ? $"Mbikëqyrja juaj e çështjes {Case(c)} përfundoi."
                : $"Mbikëqyrja juaj e çështjeve të avokatit {lawyer} përfundoi.",
            SupervisedLink);

    /// <summary>The e-mail adds where to look, since the mail has no link into the app.</summary>
    public static EmailMessage Email(string to, string title, string body) =>
        new(to, $"Avokatura e Shtetit — {title}", body + "\n\nHyni në sistem: Çështjet → \"Çështjet e ardhura\".\n\n" +
                                                  "Ky është një mesazh automatik; mos u përgjigjni.");

    public static NotificationMessage DocumentInfected(CaseSummary c) =>
        Message(NotificationTypes.DocumentInfected, c, "Dokumenti u refuzua",
            $"Një dokument i ngarkuar në çështjen {Case(c)} u refuzua nga kontrolli i sigurisë.", Tabs.Documents);

    public static NotificationMessage DocumentScanFailed(CaseSummary c) =>
        Message(NotificationTypes.DocumentScanFailed, c, "Dokumenti nuk u kontrollua",
            $"Një dokument i ngarkuar në çështjen {Case(c)} nuk mund të kontrollohej për viruse.", Tabs.Documents);

    // task 041 — reminders

    public static NotificationMessage DataMissing(CaseSummary c, bool sixMonths) =>
        Message(sixMonths ? NotificationTypes.DataMissing6M : NotificationTypes.DataMissing30D, c,
            "Të dhënat e çështjes mungojnë",
            sixMonths
                ? $"Kanë kaluar 6 muaj nga caktimi i avokatit në çështjen {Case(c)} dhe nuk janë hedhur ende të dhëna."
                : $"Kanë kaluar 30 ditë nga caktimi i avokatit në çështjen {Case(c)} dhe nuk janë hedhur ende të dhëna.",
            Tabs.Data);

    public static NotificationMessage HearingTomorrow(CaseSummary c, DateTime scheduledAtUtc) =>
        Message(NotificationTypes.HearingTomorrow, c, "Seancë nesër",
            $"Çështja {Case(c)} ka seancë nesër, më {TiraneDateTime(scheduledAtUtc)}.", Tabs.Hearings);

    public static NotificationMessage NextLevelPending(CaseSummary c, string summonsNumber, DateTime hearingAtUtc) =>
        Message(NotificationTypes.NextLevelPending3D, c, "Seanca e shkallës tjetër afron",
            $"Seanca e fletëthirrjes {summonsNumber} është më {TiraneDateTime(hearingAtUtc)}, por çështja {Case(c)} " +
            "nuk është mbyllur ende në sistem. Regjistroni vendimin.", Tabs.Decision);

    /// <summary>To the administrators; no case. Problems: e.g. "ash_audit.audit_log: p_max ka rreshta".</summary>
    public static NotificationMessage PartitionAlert(IReadOnlyList<string> problems) =>
        new(NotificationTypes.PartitionAlert, null, "Problem me particionet e bazës së të dhënave",
            $"Kontrolloni rrotullimin mujor të particioneve (ash_ops.ev_rotate_partitions): {string.Join("; ", problems)}.", null);

    private static string TiraneDateTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TiraneTime.Zone)
            .ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}
