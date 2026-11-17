namespace Ash.Modules.Audit.Contracts;

public static class AuditActions
{
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string CaseView = "CASE_VIEW";
    public const string CaseCreate = "CASE_CREATE";
    public const string CaseUpdate = "CASE_UPDATE";
    /// <summary>Task 053j: the Protokoll's country-wide "Vazhdo çështje ekzistuese" lookup (the filters used).</summary>
    public const string RegistrationLookup = "REGISTRATION_LOOKUP";
    /// <summary>Task 053j: a registration sent to another office than the one covering the summons court.</summary>
    public const string CaseOfficeOverride = "CASE_OFFICE_OVERRIDE";
    public const string DocumentDownload = "DOCUMENT_DOWNLOAD";
    public const string DocumentUpload = "DOCUMENT_UPLOAD";
    /// <summary>A document was removed (soft delete, task 034).</summary>
    public const string DocumentDelete = "DOCUMENT_DELETE";
    public const string ReportExport = "REPORT_EXPORT";
    /// <summary>Task 051: the first login at a provider linked an identity to the user (by e-mail).</summary>
    public const string IdentityLinked = "IDENTITY_LINKED";
    /// <summary>Task 051: the administrator blocked a sign-in account ("Shkëput").</summary>
    public const string IdentityUnlinked = "IDENTITY_UNLINKED";
    /// <summary>Task 051: the administrator lifted the block of a sign-in account ("Lejo sërish", with a reason).</summary>
    public const string IdentityAllowed = "IDENTITY_ALLOWED";
    /// <summary>"Raportet e mia": a saved report was created, changed or deleted (task 053h).</summary>
    public const string ReportSaved = "REPORT_SAVED";
    public const string AdminChange = "ADMIN_CHANGE";
    /// <summary>The user changed their own profile (task 048; the phone, masked).</summary>
    public const string ProfileUpdated = "PROFILE_UPDATED";
    /// <summary>Task 049: a chat message was deleted (soft; the body stays in the database).</summary>
    public const string ChatMessageDeleted = "CHAT_MESSAGE_DELETED";
    /// <summary>Task 049: someone joined or left a case chat (assignment, transfer, link, reconcile).</summary>
    public const string ChatParticipantAdded = "CHAT_PARTICIPANT_ADDED";
    public const string ChatParticipantRemoved = "CHAT_PARTICIPANT_REMOVED";
}

public static class AuditOutcomes
{
    public const string Success = "SUCCESS";
    public const string Denied = "DENIED";
    public const string Error = "ERROR";
}

/// <summary>Who performed the action. Normally taken from the current request; pass it when the user is not resolved yet.</summary>
public sealed record AuditActor(long UserId, string Display, string Roles);

public interface IAuditLog
{
    /// <summary>
    /// Queues an audit entry. Never throws and never waits for the database.
    /// Never put file contents, passwords or tokens in <paramref name="details"/>.
    /// </summary>
    void Record(
        string action,
        string? entityType = null,
        long? entityId = null,
        long? caseId = null,
        object? details = null,
        string outcome = AuditOutcomes.Success,
        AuditActor? actor = null);
}
