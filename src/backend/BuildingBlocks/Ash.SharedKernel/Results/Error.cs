namespace Ash.SharedKernel.Results;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden
}

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public const string ValidationFailedCode = "validation.failed";

    /// <summary>Extra ProblemDetails members, e.g. "errors" (field → messages) or the rows behind a conflict.</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>400 with an "errors" dictionary: camelCase field name (e.g. "firstHearing.at") → Albanian messages.</summary>
    public static Error ValidationFields(string message, IReadOnlyDictionary<string, string[]> fields) =>
        new(ValidationFailedCode, message, ErrorType.Validation)
        {
            Extensions = new Dictionary<string, object?> { ["errors"] = fields }
        };
}
