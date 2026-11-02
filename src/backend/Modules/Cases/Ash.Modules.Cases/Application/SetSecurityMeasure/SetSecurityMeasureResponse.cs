namespace Ash.Modules.Cases.Application.SetSecurityMeasure;

public sealed record SecurityMeasureView(string DecisionNumber, DateOnly DecisionDate, string? Dispositive);

/// <summary>The measure when active (null when lifted) and the new row version for the next save.</summary>
public sealed record SetSecurityMeasureResponse(uint RowVersion, bool HasSecurityMeasure, SecurityMeasureView? Measure);
