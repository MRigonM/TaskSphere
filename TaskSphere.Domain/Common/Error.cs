namespace TaskSphere.Domain.Common;

/// <summary>
/// Represents an error with a code and description.
/// </summary>
public record Error(string Code, string Description)
{
    public string Code { get; } = Code;
    public string Description { get; } = Description;

    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("General.Null", "Null value was provided");

    public static Error NotFound(string entity) => new("NotFound", $"{entity} was not found.");
    public static Error Forbidden() => new("Auth.Forbidden", "You do not have permission to perform this action.");
    public static Error Conflict(string message) => new("Conflict", message);
    public static Error Validation(string field, string message) => new("Validation", $"{field}: {message}");
}