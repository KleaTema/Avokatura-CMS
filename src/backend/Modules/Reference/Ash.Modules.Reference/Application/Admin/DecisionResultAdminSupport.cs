using System.Text.RegularExpressions;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Reference.Application.Admin;

public sealed record CreateDecisionResultRequest(
    string Code, string Name, string OutcomeGroup, bool? SendsBackForRetrial, int SortOrder, bool IsOther);

public sealed record UpdateDecisionResultRequest(string Name, int SortOrder, bool IsOther);

public sealed record DecisionResultAdminResponse(
    string Code, string Name, string OutcomeGroup, bool SendsBackForRetrial, bool IsOther, byte SortOrder, bool IsActive);

public static partial class DecisionResultAdminSupport
{
    private static readonly string[] OutcomeGroups = ["WIN", "LOSS", "PARTIAL", "PROCEDURAL", "OTHER"];

    public static Error? ValidateCreate(string? code, string? name, string? outcomeGroup, bool? sendsBackForRetrial, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 40 || !UpperSnakeRegex().IsMatch(code.Trim()))
        {
            return Error.Validation("decision_result.code_invalid",
                "Kodi është i detyrueshëm, duhet të jetë UPPER_SNAKE dhe të ketë maksimum 40 karaktere.");
        }

        if (string.IsNullOrWhiteSpace(outcomeGroup) || !OutcomeGroups.Contains(outcomeGroup.Trim()))
        {
            return Error.Validation("decision_result.outcome_group_invalid",
                "Grupi i rezultatit është i detyrueshëm dhe duhet të jetë WIN, LOSS, PARTIAL, PROCEDURAL ose OTHER.");
        }

        if (sendsBackForRetrial is null)
        {
            return Error.Validation("decision_result.sends_back_for_retrial_required",
                "Duhet të përcaktohet nëse rezultati kthen çështjen për rigjykim.");
        }

        return ValidateUpdate(name, sortOrder);
    }

    public static Error? ValidateUpdate(string? name, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            return Error.Validation("decision_result.name_invalid",
                "Emri është i detyrueshëm (deri në 150 karaktere).");
        }

        if (sortOrder is < 0 or > byte.MaxValue)
        {
            return Error.Validation("decision_result.sort_order_invalid",
                "Renditja duhet të jetë në intervalin 0-255.");
        }

        return null;
    }

    public static byte ToDbSortOrder(int sortOrder) => checked((byte)sortOrder);

    public static DecisionResultAdminResponse ToResponse(DecisionResultRecord record) =>
        new(record.Code, record.Name, record.OutcomeGroup, record.SendsBackForRetrial, record.IsOther, record.SortOrder, record.IsActive);

    public static object Snapshot(DecisionResultRecord record) => new
    {
        Code = record.Code,
        Name = record.Name,
        OutcomeGroup = record.OutcomeGroup,
        SendsBackForRetrial = record.SendsBackForRetrial,
        SortOrder = record.SortOrder,
        IsOther = record.IsOther,
        IsActive = record.IsActive
    };

    public static void AuditChange(IAuditLog audit, DecisionResultRecord record, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "DECISION_RESULT", record.Id,
            details: new { action, old = oldValues, @new = newValues });

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex UpperSnakeRegex();
}
