using System.Text.RegularExpressions;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Reference.Application.Admin;

public sealed record CreateHearingTypeRequest(string Code, string Name, int SortOrder, bool IsOther);

public sealed record UpdateHearingTypeRequest(string Name, int SortOrder, bool IsOther);

public sealed record HearingTypeAdminResponse(string Code, string Name, bool IsOther, byte SortOrder, bool IsActive);

public static partial class HearingTypeAdminSupport
{
    public static Error? ValidateCreate(string? code, string? name, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 30 || !UpperSnakeRegex().IsMatch(code.Trim()))
        {
            return Error.Validation("hearing_type.code_invalid",
                "Kodi është i detyrueshëm, duhet të jetë UPPER_SNAKE dhe të ketë maksimum 30 karaktere.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return Error.Validation("hearing_type.name_invalid",
                "Emri është i detyrueshëm (deri në 100 karaktere).");
        }

        if (sortOrder is < 0 or > byte.MaxValue)
        {
            return Error.Validation("hearing_type.sort_order_invalid",
                "Renditja duhet të jetë në intervalin 0-255.");
        }

        return null;
    }

    public static Error? ValidateUpdate(string? name, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return Error.Validation("hearing_type.name_invalid",
                "Emri është i detyrueshëm (deri në 100 karaktere).");
        }

        if (sortOrder is < 0 or > byte.MaxValue)
        {
            return Error.Validation("hearing_type.sort_order_invalid",
                "Renditja duhet të jetë në intervalin 0-255.");
        }

        return null;
    }

    public static byte ToDbSortOrder(int sortOrder) => checked((byte)sortOrder);

    public static HearingTypeAdminResponse ToResponse(HearingTypeRecord record) =>
        new(record.Code, record.Name, record.IsOther, record.SortOrder, record.IsActive);

    public static object Snapshot(HearingTypeRecord record) => new
    {
        Code = record.Code,
        Name = record.Name,
        SortOrder = record.SortOrder,
        IsOther = record.IsOther,
        IsActive = record.IsActive
    };

    public static void AuditChange(IAuditLog audit, HearingTypeRecord record, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "HEARING_TYPE", record.Id,
            details: new { action, old = oldValues, @new = newValues });

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex UpperSnakeRegex();
}
