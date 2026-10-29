using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application;

/// <summary>
/// Collects field errors under camelCase request paths ("firstHearing.at", "parties[1].identifier") so the UI can
/// show each message next to its field; becomes one 400 with an "errors" dictionary.
/// </summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool Any => _errors.Count > 0;

    public bool Has(string field) => _errors.ContainsKey(field);

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(message);
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(x => x.Key, x => x.Value.ToArray());

    public Error ToError(string message) => Error.ValidationFields(message, ToDictionary());
}
