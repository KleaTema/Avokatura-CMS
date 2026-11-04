using Ash.SharedKernel.Events;

namespace Ash.Modules.Documents.Contracts.Events;

/// <summary>
/// The virus scan or the format check rejected an upload (task 033); the file stays in quarantine and expires.
/// Notifications (task 040) tell the uploader and the active administrators — the title and the reason stay out of the
/// notification text (only the case code and court).
/// </summary>
public sealed record DocumentInfected(Guid DocumentPublicId, long CaseId, long UploadedByUserId, string Title, string Reason) : IEvent;

/// <summary>
/// The scanner was unavailable for every attempt; the document is FAILED. Notifications (task 040) tell the uploader
/// and the active administrators.
/// </summary>
public sealed record DocumentScanFailed(Guid DocumentPublicId, long CaseId, long UploadedByUserId, string Title) : IEvent;

/// <summary>
/// The scan passed: the document is CLEAN and moved to the documents bucket (task 040: pushed to the uploader as
/// "document.status", not stored as a notification). Published in the scan job's transaction.
/// </summary>
public sealed record DocumentScanned(Guid DocumentPublicId, long UploadedByUserId, string ScanStatus) : IEvent;
