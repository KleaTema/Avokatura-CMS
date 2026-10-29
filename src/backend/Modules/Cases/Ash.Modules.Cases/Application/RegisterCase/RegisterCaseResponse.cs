namespace Ash.Modules.Cases.Application.RegisterCase;

/// <summary>Non-blocking findings, e.g. a judge listed at another court or a hearing date in the past.</summary>
public sealed record RegistrationWarning(string Code, string Message);

public sealed record RegisterCaseResponse(Guid CasePublicId, string CaseCode, IReadOnlyList<RegistrationWarning> Warnings);

/// <summary>A case that may be the same summons; only cases the user may see are listed.</summary>
public sealed record PossibleDuplicate(string CaseCode, string Status, string CourtName, DateTime RegisteredAt);
