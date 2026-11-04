using System.Globalization;
using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Audit.Application.Digest;
using Ash.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Audit.Endpoints;

/// <summary>Task 041: the audit chain check, for administrators (audit.view is held by ADMIN only).</summary>
public static class AuditAdminEndpoints
{
    public const string Permission = "audit.view";

    public static IEndpointRouteBuilder MapAuditAdmin(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/audit/verify",
                async (string? from, string? to, VerifyAuditChain verify, CancellationToken ct) =>
                {
                    var fields = new Dictionary<string, string[]>();
                    var start = Parse(from, "from", fields);
                    var end = Parse(to, "to", fields);
                    if (fields.Count > 0)
                    {
                        return Error.ValidationFields("Periudha nuk është e vlefshme.", fields).ToProblem();
                    }

                    var result = await verify.VerifyAsync(start!.Value, end!.Value, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(Permission)
            .Produces<AuditChainVerification>()
            .ProducesValidationProblem()
            .WithName("VerifyAuditChain");
        return endpoints;
    }

    /// <summary>A calendar date yyyy-MM-dd (a Tirane day of the audit log).</summary>
    private static DateOnly? Parse(string? value, string field, Dictionary<string, string[]> fields)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        fields[field] = ["Shkruani datën në formën yyyy-MM-dd."];
        return null;
    }
}
