using Ash.Modules.Cases.Contracts;

namespace Ash.Modules.Cases.Infrastructure.History;

public static class HistoryEventCodes
{
    public static string ToCode(HistoryEventType type) => type switch
    {
        HistoryEventType.DossierCreated => "DOSSIER_CREATED",
        HistoryEventType.CaseRegistered => "CASE_REGISTERED",
        HistoryEventType.CaseCreatedFromPrevious => "CASE_CREATED_FROM_PREVIOUS",
        HistoryEventType.NextLevelSummonsPending => "NEXT_LEVEL_SUMMONS_PENDING",
        HistoryEventType.SentToHead => "SENT_TO_HEAD",
        HistoryEventType.LawyerAssigned => "LAWYER_ASSIGNED",
        HistoryEventType.LawyerTransferred => "LAWYER_TRANSFERRED",
        HistoryEventType.SecondaryAssigned => "SECONDARY_ASSIGNED",
        HistoryEventType.AssistantAdded => "ASSISTANT_ADDED",
        HistoryEventType.AssistantRemoved => "ASSISTANT_REMOVED",
        HistoryEventType.PriorityChanged => "PRIORITY_CHANGED",
        HistoryEventType.SuspensionChanged => "SUSPENSION_CHANGED",
        HistoryEventType.CategoryChanged => "CATEGORY_CHANGED",
        HistoryEventType.Delegated => "DELEGATED",
        HistoryEventType.InstitutionLinked => "INSTITUTION_LINKED",
        HistoryEventType.InstitutionUnlinked => "INSTITUTION_UNLINKED",
        HistoryEventType.JuristAssigned => "JURIST_ASSIGNED",
        HistoryEventType.JudgeChanged => "JUDGE_CHANGED",
        HistoryEventType.ObjectChanged => "OBJECT_CHANGED",
        HistoryEventType.PartyAdded => "PARTY_ADDED",
        HistoryEventType.PartyUpdated => "PARTY_UPDATED",
        HistoryEventType.PartyRemoved => "PARTY_REMOVED",
        HistoryEventType.HearingAdded => "HEARING_ADDED",
        HistoryEventType.HearingUpdated => "HEARING_UPDATED",
        HistoryEventType.DocumentUploaded => "DOCUMENT_UPLOADED",
        HistoryEventType.DocumentRemoved => "DOCUMENT_REMOVED",
        HistoryEventType.SecurityMeasureSet => "SECURITY_MEASURE_SET",
        HistoryEventType.DecisionRecorded => "DECISION_RECORDED",
        HistoryEventType.DecisionUpdated => "DECISION_UPDATED",
        HistoryEventType.CaseClosed => "CASE_CLOSED",
        HistoryEventType.DossierArchived => "DOSSIER_ARCHIVED",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped history event type."),
    };
}
