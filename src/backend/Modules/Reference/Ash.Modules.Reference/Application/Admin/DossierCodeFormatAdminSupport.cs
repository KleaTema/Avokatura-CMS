using System.Text.RegularExpressions;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed record CreateDossierCodeFormatRequest(string Template, int RandomDigits, string? Note);

public sealed record DossierCodeFormatAdminResponse(byte Id, string Template, byte RandomDigits, bool IsActive, DateTime CreatedAt, string? Note);

public static partial class DossierCodeFormatAdminSupport
{
    private static readonly string[] AllowedTokens = ["{YYYY}", "{OFFICE}", "{RAND}"];
    private const int MaxExpandedLength = 30;
    private const int MaxTemplateLength = 40;

    public static async Task<Error?> ValidateCreate(ReferenceDbContext db, string? template, int? randomDigits, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return Error.Validation("dossier_code_format.template_required", "Template-i është i detyrueshëm.");
        }

        var trimmed = template.Trim();
        if (!IsValidTemplate(trimmed))
        {
            return Error.Validation("dossier_code_format.template_invalid",
                "Template-i përmban token të pavlefshëm. Lejohen vetëm {YYYY}, {OFFICE} dhe {RAND}.");
        }

        if (randomDigits is null || randomDigits < 4 || randomDigits > 12)
        {
            return Error.Validation("dossier_code_format.random_digits_invalid",
                "Vlera e random_digits duhet të jetë ndërmjet 4 dhe 12.");
        }

        if (trimmed.Length > MaxTemplateLength)
        {
            return Error.Validation("dossier_code_format.template_too_long",
                "Template-i nuk mund të ketë më shumë se 40 karaktere.");
        }

        var longestOfficeCode = await db.Database
            .SqlQuery<long>($"SELECT COALESCE(MAX(CHAR_LENGTH(code)), 0) AS Value FROM offices")
            .SingleAsync(ct);
        var expanded = ExpandTemplate(trimmed, new string('X', (int)longestOfficeCode), randomDigits.Value);
        if (expanded.Length > MaxExpandedLength)
        {
            return Error.Validation("dossier_code_format.code_too_long",
                "Kodi do të ishte më i gjatë se 30 karaktere.");
        }

        return null;
    }

    public static Error? ValidateActivate(string? template, int? randomDigits)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return Error.Validation("dossier_code_format.template_required", "Template-i është i detyrueshëm.");
        }

        if (!IsValidTemplate(template.Trim()))
        {
            return Error.Validation("dossier_code_format.template_invalid",
                "Template-i përmban token të pavlefshëm. Lejohen vetëm {YYYY}, {OFFICE} dhe {RAND}.");
        }

        if (randomDigits is null || randomDigits < 4 || randomDigits > 12)
        {
            return Error.Validation("dossier_code_format.random_digits_invalid",
                "Vlera e random_digits duhet të jetë ndërmjet 4 dhe 12.");
        }

        return null;
    }

    public static DossierCodeFormatAdminResponse ToResponse(DossierCodeFormatRecord record) =>
        new(record.Id, record.Template, record.RandomDigits, record.IsActive, record.CreatedAt, record.Note);

    public static object Snapshot(DossierCodeFormatRecord record) => new
    {
        record.Template,
        RandomDigits = record.RandomDigits,
        record.IsActive,
        record.Note,
        record.CreatedAt
    };

    public static void AuditChange(IAuditLog audit, DossierCodeFormatRecord record, string action, object? oldValues, object? newValues) =>
        audit.Record(AuditActions.AdminChange, "DOSSIER_CODE_FORMAT", record.Id,
            details: new { action, old = oldValues, @new = newValues });

    public static bool IsValidTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var matches = TokenRegex().Matches(template);
        if (matches.Count == 0)
        {
            return false;
        }

        var tokens = matches.Select(x => x.Value).ToArray();
        if (tokens.Any(x => !AllowedTokens.Contains(x)))
        {
            return false;
        }

        if (tokens.Count(x => x == "{RAND}") != 1)
        {
            return false;
        }

        if (template.Contains("{", StringComparison.Ordinal) || template.Contains("}", StringComparison.Ordinal))
        {
            var unmatched = Regex.Matches(template, "\\{[^}]*\\}");
            if (unmatched.Count > 0)
            {
                foreach (Match match in unmatched)
                {
                    if (!AllowedTokens.Contains(match.Value))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public static string ExpandTemplate(string template, string officeCode, int randomDigits)
    {
        var result = template.Replace("{YYYY}", "2024", StringComparison.Ordinal);
        result = result.Replace("{OFFICE}", officeCode, StringComparison.Ordinal);
        result = result.Replace("{RAND}", new string('9', randomDigits), StringComparison.Ordinal);
        return result;
    }

    [GeneratedRegex("\\{[A-Z_]+\\}")]
    private static partial Regex TokenRegex();
}
