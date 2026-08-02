using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace TaskSphere.Domain.Common;

public readonly partial record struct TaskKey(string ProjectKey, int Number)
{
    [GeneratedRegex(@"^([A-Z][A-Z0-9]{1,9})-([1-9]\d{0,8})\z")]
    private static partial Regex FullKeyPattern();

    [GeneratedRegex(@"^[A-Z][A-Z0-9]{1,9}\z")]
    private static partial Regex ProjectKeyPattern();

    public static bool TryParse([NotNullWhen(true)] string? input, out TaskKey key)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = FullKeyPattern().Match(input);
        if (!match.Success)
            return false;

        key = new TaskKey(match.Groups[1].Value, int.Parse(match.Groups[2].Value));
        return true;
    }

    public static bool IsValidProjectKey([NotNullWhen(true)] string? value)
        => !string.IsNullOrEmpty(value) && ProjectKeyPattern().IsMatch(value);

    public override string ToString() => $"{ProjectKey}-{Number}";
}
