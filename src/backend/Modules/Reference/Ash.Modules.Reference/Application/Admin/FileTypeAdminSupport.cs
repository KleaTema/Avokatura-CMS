using System.Text.RegularExpressions;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Reference.Application.Admin;

public sealed record CreateFileTypeRequest(
    string Extension,
    string ContentType,
    string MagicHex,
    long MaxSizeBytes,
    bool IsActive,
    string AppliesTo);

public sealed record UpdateFileTypeRequest(
    string Extension,
    string ContentType,
    string MagicHex,
    long MaxSizeBytes,
    bool IsActive,
    string AppliesTo);

public sealed record FileTypeAdminResponse(
    byte Id,
    string Extension,
    string ContentType,
    string MagicHex,
    ulong MaxSizeBytes,
    string AppliesTo,
    bool IsActive);

public static partial class FileTypeAdminSupport
{
    private static readonly string[] BlockedExtensions =
    [
        "exe", "dll", "bat", "cmd", "com", "msi", "ps1", "vbs", "js", "jar", "sh", "html", "htm", "svg", "xml"
    ];

    public static Error? ValidateCreate(string? extension, string? contentType, string? magicHex, long maxSizeBytes, string? appliesTo)
    {
        var validation = ValidateCore(extension, contentType, magicHex, maxSizeBytes, appliesTo);
        if (validation is not null)
        {
            return validation;
        }

        return null;
    }

    public static Error? ValidateUpdate(string? extension, string? contentType, string? magicHex, long maxSizeBytes, string? appliesTo)
    {
        var validation = ValidateCore(extension, contentType, magicHex, maxSizeBytes, appliesTo);
        if (validation is not null)
        {
            return validation;
        }

        return null;
    }

    public static Error? ValidateCore(string? extension, string? contentType, string? magicHex, long maxSizeBytes, string? appliesTo)
    {
        if (string.IsNullOrWhiteSpace(extension) || !LowercaseExtensionRegex().IsMatch(extension.Trim()))
        {
            return Error.Validation("file_type.extension_invalid",
                "Përkufizimi i shtesës është i detyrueshëm dhe duhet të jetë i vogël, pa pikë dhe pa karaktere speciale.");
        }

        var cleaned = extension.Trim();
        if (BlockedExtensions.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
        {
            return Error.Validation("file_type.blocked_extension",
                $"Shtesa '{cleaned}' nuk lejohet. Vendosni një lloj skedari të sigurt.");
        }

        if (string.IsNullOrWhiteSpace(contentType) || !ContentTypeRegex().IsMatch(contentType.Trim()))
        {
            return Error.Validation("file_type.content_type_invalid",
                "Lloji i MIME-së është i detyrueshëm dhe duhet të jetë një string standard i tipit MIME.");
        }

        if (string.IsNullOrWhiteSpace(magicHex) || !MagicHexRegex().IsMatch(magicHex.Trim()) || magicHex.Trim().Length % 2 != 0 || magicHex.Trim().Length > 32)
        {
            return Error.Validation("file_type.magic_hex_invalid",
                "Magic hex duhet të jetë një varg me gjatësi çift të hex-it, me maksimum 32 karaktere.");
        }

        // TODO(client-max-file-size)
        if (maxSizeBytes <= 0 || maxSizeBytes > 104857600)
        {
            return Error.Validation("file_type.max_size_invalid",
                "Madhësia maksimale duhet të jetë nga 1 bajt deri në 100 MB.");
        }

        if (string.IsNullOrWhiteSpace(appliesTo) || !AppliesToValues.Contains(appliesTo.Trim()))
        {
            return Error.Validation("file_type.applies_to_invalid",
                "Vlera 'applies_to' është e detyrueshme dhe duhet të jetë DOCUMENT, CHAT ose BOTH.");
        }

        return null;
    }

    public static FileTypeAdminResponse ToResponse(AllowedFileTypeRecord record) =>
        new(record.Id, record.Extension, record.ContentType, record.MagicHex, record.MaxSizeBytes, record.AppliesTo, record.IsActive);

    public static object Snapshot(AllowedFileTypeRecord record) => new
    {
        record.Extension,
        record.ContentType,
        record.MagicHex,
        MaxSizeBytes = record.MaxSizeBytes,
        record.AppliesTo,
        record.IsActive
    };

    public static void AuditChange(IAuditLog audit, AllowedFileTypeRecord record, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "FILE_TYPE", record.Id,
            details: new { action, old = oldValues, @new = newValues });

    private static readonly HashSet<string> AppliesToValues =
    [
        "DOCUMENT",
        "CHAT",
        "BOTH"
    ];

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex LowercaseExtensionRegex();

    [GeneratedRegex("^[A-Za-z0-9!#$%&'*+.^_`|~-]+/[A-Za-z0-9!#$%&'*+.^_`|~-]+$")]
    private static partial Regex ContentTypeRegex();

    [GeneratedRegex("^[0-9a-fA-F]+$")]
    private static partial Regex MagicHexRegex();
}
