using Ash.Modules.Cases.Application.RegisterCase;

namespace Ash.Modules.Cases.Application.ChangeJudge;

public sealed record JudgeView(long Id, string Name);

/// <summary>The new judge, the new row version, and warnings (e.g. judge.court_mismatch) to show, not to block.</summary>
public sealed record ChangeJudgeResponse(uint RowVersion, JudgeView Judge, IReadOnlyList<RegistrationWarning> Warnings);
