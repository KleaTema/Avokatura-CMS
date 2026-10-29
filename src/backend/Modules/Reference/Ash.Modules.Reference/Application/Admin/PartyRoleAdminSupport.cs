using System.Text.RegularExpressions;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Reference.Application.Admin;

public sealed record CreatePartyRoleRequest(string Code, string Name, string ReportGroup, int SortOrder);

public sealed record UpdatePartyRoleRequest(string Name, int SortOrder);

public sealed record PartyRoleAdminResponse(string Code, string Name, string ReportGroup, byte SortOrder, bool IsActive);

public static partial class PartyRoleAdminSupport
{
    private static readonly string[] ReportGroups = ["PLAINTIFF", "DEFENDANT_OR_THIRD"];

    public static Error? ValidateCreate(string? code, string? name, string? reportGroup, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 30 || !UpperSnakeRegex().IsMatch(code.Trim()))
        {
            return Error.Validation("party_role.code_invalid",
                "Kodi është i detyrueshëm, duhet të jetë UPPER_SNAKE dhe të ketë maksimum 30 karaktere.");
        }

        if (string.IsNullOrWhiteSpace(reportGroup) || !ReportGroups.Contains(reportGroup.Trim()))
        {
            return Error.Validation("party_role.report_group_invalid",
                "Grupi i raportit është i detyrueshëm dhe duhet të jetë PLAINTIFF ose DEFENDANT_OR_THIRD.");
        }

        return ValidateUpdate(name, sortOrder);
    }

    public static Error? ValidateUpdate(string? name, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return Error.Validation("party_role.name_invalid",
                "Emri është i detyrueshëm (deri në 100 karaktere).");
        }

        if (sortOrder is < 0 or > byte.MaxValue)
        {
            return Error.Validation("party_role.sort_order_invalid",
                "Renditja duhet të jetë në intervalin 0-255.");
        }

        return null;
    }

    public static byte ToDbSortOrder(int sortOrder) => checked((byte)sortOrder);

    public static PartyRoleAdminResponse ToResponse(PartyRoleRecord record) =>
        new(record.Code, record.Name, record.ReportGroup, record.SortOrder, record.IsActive);

    public static object Snapshot(PartyRoleRecord record) => new
    {
        Code = record.Code,
        Name = record.Name,
        ReportGroup = record.ReportGroup,
        SortOrder = record.SortOrder,
        IsActive = record.IsActive
    };

    public static void AuditChange(IAuditLog audit, PartyRoleRecord record, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "PARTY_ROLE", record.Id,
            details: new { action, old = oldValues, @new = newValues });

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex UpperSnakeRegex();
}
