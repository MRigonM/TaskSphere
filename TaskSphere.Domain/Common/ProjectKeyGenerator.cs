namespace TaskSphere.Domain.Common;

/// <summary>
/// Derives a project key from a project name. Used only by the one-off backfill —
/// keys for new projects are supplied by the user.
/// </summary>
public static class ProjectKeyGenerator
{
    private const int MaxLength = 10;

    public static string Derive(string projectName, IReadOnlySet<string> taken)
    {
        var baseKey = BuildBase(projectName);

        if (!taken.Contains(baseKey))
            return baseKey;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var suffixText = suffix.ToString();
            var candidate = Truncate(baseKey, MaxLength - suffixText.Length) + suffixText;

            if (!taken.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException($"Could not derive a unique key for project '{projectName}'.");
    }

    private static string BuildBase(string projectName)
    {
        var cleaned = new string((projectName ?? "").Where(char.IsAsciiLetterOrDigit).ToArray());
        var capitals = new string(cleaned.Where(char.IsAsciiLetterUpper).ToArray());

        var candidate = capitals.Length >= 2
            ? Truncate(capitals, MaxLength)
            : Truncate(cleaned.ToUpperInvariant(), 3);

        if (candidate.Length == 0)
            return "PRJ";

        if (!char.IsAsciiLetter(candidate[0]))
            candidate = Truncate("P" + candidate, MaxLength);

        if (candidate.Length < 2)
            candidate += "X";

        return candidate;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
