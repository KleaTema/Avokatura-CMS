using Ash.SharedKernel.Events;

namespace Ash.Modules.Documents.Contracts.Events;

/// <summary>
/// The virus scan or the format check rejected an upload (task 033); the file stays in quarantine and expires.
/// TODO(task-040): notify the uploader and the administrators.
/// </summary>
public sealed record DocumentInfected(Guid DocumentPublicId, long CaseId, long UploadedByUserId, string Title, string Reason) : IEvent;

/// <summary>The scanner was unavailable for every attempt; the document is FAILED. TODO(task-040): notify the administrators.</summary>
public sealed record DocumentScanFailed(Guid DocumentPublicId, long CaseId, long UploadedByUserId, string Title) : IEvent;
