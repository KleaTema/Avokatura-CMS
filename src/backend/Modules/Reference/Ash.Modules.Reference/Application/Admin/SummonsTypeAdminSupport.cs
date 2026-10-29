using System.Text.RegularExpressions;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Reference.Application.Admin;

public sealed record CreateSummonsTypeRequest(string Code, string Name);

public sealed record UpdateSummonsTypeRequest(string Name);

public sealed record SummonsTypeAdminResponse(string Code, string Name, bool IsActive);

public static partial class SummonsTypeAdminSupport
{
    public static Error? ValidateCreate(string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 30 || !UpperSnakeRegex().IsMatch(code.Trim()))
        {
            return Error.Validation("summons_type.code_invalid",
                "Kodi është i detyrueshëm, duhet të jetë UPPER_SNAKE dhe të ketë maksimum 30 karaktere.");
        }

        return ValidateUpdate(name);
    }

    public static Error? ValidateUpdate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return Error.Validation("summons_type.name_invalid",
                "Emri është i detyrueshëm (deri në 100 karaktere).");
        }

        return null;
    }

    public static SummonsTypeAdminResponse ToResponse(SummonsTypeRecord record) =>
        new(record.Code, record.Name, record.IsActive);

    public static object Snapshot(SummonsTypeRecord record) => new
    {
        Code = record.Code,
        Name = record.Name,
        IsActive = record.IsActive
    };

    public static void AuditChange(IAuditLog audit, SummonsTypeRecord record, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "SUMMONS_TYPE", record.Id,
            details: new { action, old = oldValues, @new = newValues });

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex UpperSnakeRegex();
}
