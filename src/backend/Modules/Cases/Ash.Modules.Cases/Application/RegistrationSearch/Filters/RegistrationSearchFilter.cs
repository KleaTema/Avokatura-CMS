using Ash.Infrastructure.Persistence;
using Ash.Modules.Reference.Contracts;

namespace Ash.Modules.Cases.Application.RegistrationSearch.Filters;

/// <summary>Which control the page shows for a filter: the frontend has one renderer per input (task 053b).</summary>
public enum RegistrationFilterInput
{
    Text,
    CourtLevel,
    Court,
    Judge,
    Category,
    Subcategory,
    DecisionResult
}

/// <summary>A filter value bound to SQL: a predicate over ONE level of the dossier (alias <c>a</c>) and its parameters.</summary>
internal sealed record FilterSql(string Predicate, IReadOnlyDictionary<string, object> Parameters);

/// <summary>Either the SQL of a valid value or the 400 message for this field.</summary>
internal sealed record FilterBinding(FilterSql? Sql, string? Error)
{
    public static FilterBinding Invalid(string message) => new(null, message);

    public static FilterBinding Of(string predicate, params (string Name, object Value)[] parameters) =>
        new(new FilterSql(predicate, parameters.ToDictionary(p => p.Name, p => p.Value)), null);

    /// <summary>A valid value that cannot match anything (e.g. text with no searchable word).</summary>
    public static FilterBinding NoMatch { get; } = Of("FALSE");
}

/// <summary>
/// One field of the "Vazhdo çështje ekzistuese" search (client decision B.2, 12.11.2026). A subclass is the whole
/// filter: its query parameter <see cref="Name"/>, the UI metadata (<see cref="Input"/>, <see cref="DependsOn"/>),
/// and the predicate. <see cref="RegistrationSearchFilters"/> finds every subclass by reflection, so adding a filter
/// is one class here plus one entry in the frontend (docs/architecture.md, "Registration search filters").
/// </summary>
internal abstract class RegistrationSearchFilter
{
    /// <summary>The query parameter, also the key of the 400 field error and of the UI entry.</summary>
    public abstract string Name { get; }

    /// <summary>The position in the form (and in the OpenAPI parameters).</summary>
    public abstract int Order { get; }

    public abstract RegistrationFilterInput Input { get; }

    /// <summary>The filter whose value narrows this one's choices in the UI (judge → court, subcategory → category).</summary>
    public virtual string? DependsOn => null;

    /// <summary>The OpenAPI type of the parameter.</summary>
    public virtual bool IsInteger => false;

    /// <summary>Validates the raw value; the parameters are named after <see cref="Name"/> (unique per filter).</summary>
    public abstract Task<FilterBinding> BindAsync(string value, IReferenceData reference, CancellationToken ct);
}

/// <summary>
/// Free text (live search, <see cref="TextSearch"/>): every term a substring, from 2 characters, accent-insensitive;
/// the ngram FULLTEXT index (script 014) narrows from 2 characters, the escaped LIKE decides. The parameters are named
/// after <see cref="RegistrationSearchFilter.Name"/>.
/// </summary>
internal abstract class TextFilter : RegistrationSearchFilter
{
    public const int MaxLength = TextSearch.MaxLength;

    public override RegistrationFilterInput Input => RegistrationFilterInput.Text;

    /// <summary>The predicate of <paramref name="search"/> on this filter's columns (search.Predicate(name, …)).</summary>
    protected abstract string Predicate(TextSearch search, string name);

    public override Task<FilterBinding> BindAsync(string value, IReferenceData reference, CancellationToken ct)
    {
        var search = TextSearch.Parse(value);
        if (search.Text.Length > MaxLength)
        {
            return Task.FromResult(FilterBinding.Invalid($"Deri në {MaxLength} karaktere."));
        }

        if (search.IsEmpty)
        {
            // only 1-character terms (TextSearch.MinTermLength): no filter, as an empty field
            return Task.FromResult(FilterBinding.Of("TRUE"));
        }

        return Task.FromResult(FilterBinding.Of(
            Predicate(search, Name),
            search.Parameters(Name).Select(p => (p.ParameterName, (object)p.Value!)).ToArray()));
    }
}

/// <summary>A party name or NIPT, among the parties whose role lands in <see cref="ReportGroup"/> (party_roles.report_group).</summary>
internal abstract class PartyFilter : TextFilter
{
    protected abstract string ReportGroup { get; }

    protected override string Predicate(TextSearch search, string name) => $"""
        a.id IN (SELECT p.case_id FROM case_parties p JOIN party_roles r ON r.id = p.party_role_id
                 WHERE p.deleted_at IS NULL AND r.report_group = '{ReportGroup}'
                   AND {search.Predicate(name, "p.display_name", "p.identifier")})
        """;
}

/// <summary>A reference id chosen from a list (court, judge).</summary>
internal abstract class IdFilter : RegistrationSearchFilter
{
    public override bool IsInteger => true;

    protected abstract string Column { get; }

    protected abstract string InvalidMessage { get; }

    public override Task<FilterBinding> BindAsync(string value, IReferenceData reference, CancellationToken ct) =>
        Task.FromResult(uint.TryParse(value.Trim(), out var id) && id > 0
            ? FilterBinding.Of($"a.{Column} = @{Name}", ($"@{Name}", id))
            : FilterBinding.Invalid(InvalidMessage));
}

/// <summary>A reference code chosen from a list (category, subcategory, result), resolved to its id.</summary>
internal abstract class CodeFilter : RegistrationSearchFilter
{
    protected abstract string InvalidMessage { get; }

    protected abstract Task<byte?> ResolveAsync(string code, IReferenceData reference, CancellationToken ct);

    protected abstract string Predicate(string id);

    public override async Task<FilterBinding> BindAsync(string value, IReferenceData reference, CancellationToken ct) =>
        await ResolveAsync(value.Trim(), reference, ct) is { } id
            ? FilterBinding.Of(Predicate($"@{Name}"), ($"@{Name}", id))
            : FilterBinding.Invalid(InvalidMessage);
}
