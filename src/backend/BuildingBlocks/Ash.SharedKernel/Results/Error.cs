namespace Ash.SharedKernel.Results;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    /// <summary>413: an upload above its size limit (task 033).</summary>
    TooLarge,
    /// <summary>429: a rate limit (task 049: chat messages per minute).</summary>
    TooManyRequests
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
    public static Error TooLarge(string code, string message) => new(code, message, ErrorType.TooLarge);
    public static Error TooManyRequests(string code, string message) => new(code, message, ErrorType.TooManyRequests);

    /// <summary>400 with an "errors" dictionary: camelCase field name (e.g. "firstHearing.at") → Albanian messages.</summary>
    public static Error ValidationFields(string message, IReadOnlyDictionary<string, string[]> fields) =>
        new(ValidationFailedCode, message, ErrorType.Validation)
        {
            Extensions = new Dictionary<string, object?> { ["errors"] = fields }
        };
}
