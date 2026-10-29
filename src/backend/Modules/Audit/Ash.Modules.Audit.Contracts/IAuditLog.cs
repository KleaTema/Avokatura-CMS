namespace Ash.Modules.Audit.Contracts;

public static class AuditActions
{
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string CaseView = "CASE_VIEW";
    public const string CaseCreate = "CASE_CREATE";
    public const string CaseUpdate = "CASE_UPDATE";
    public const string DocumentDownload = "DOCUMENT_DOWNLOAD";
    public const string DocumentUpload = "DOCUMENT_UPLOAD";
    public const string ReportExport = "REPORT_EXPORT";
    public const string AdminChange = "ADMIN_CHANGE";
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
