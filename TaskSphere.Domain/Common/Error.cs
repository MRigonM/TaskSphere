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
}