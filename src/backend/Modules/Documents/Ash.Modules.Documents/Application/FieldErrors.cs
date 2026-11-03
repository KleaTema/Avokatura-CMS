using Ash.SharedKernel.Results;

namespace Ash.Modules.Documents.Application;

/// <summary>Field errors under camelCase request names → one 400 with an "errors" dictionary (as in the Cases module).</summary>
internal sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(message);
    }

    public Error ToError(string message) =>
        Error.ValidationFields(message, _errors.ToDictionary(x => x.Key, x => x.Value.ToArray()));
}
