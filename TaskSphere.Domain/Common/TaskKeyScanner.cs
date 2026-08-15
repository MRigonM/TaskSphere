using System.Text.RegularExpressions;

namespace TaskSphere.Domain.Common;

/// <summary>
/// Finds task keys inside free text — commit messages, branch names, pull request titles and
/// bodies.
/// <para>
/// This exists because <see cref="TaskKey.TryParse"/> is anchored <c>^…\z</c>: it answers "is
/// this whole string a key", which is right for the quick-jump box and useless for a commit
/// message. The boundary rules live here; what counts as a *valid* key stays in
/// <see cref="TaskKey"/>, which every candidate is still parsed through.
/// </para>
/// </summary>
public static partial class TaskKeyScanner
{
    // Boundaries, not \b: \b would happily match the "TS-42" inside "XTS-42", because the
    // boundary it finds sits between X and T only when one of them is a non-word character.
    // The trailing (?![0-9]) stops TS-421 being read as TS-42.
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z][A-Z0-9]{1,9}-[1-9][0-9]{0,8}(?![0-9])")]
    private static partial Regex CandidatePattern();

    /// <summary>
    /// Every distinct key named by <paramref name="text"/>, in the order it first appears.
    /// Unparseable text is not an error — an unresolvable key is normal traffic.
    /// </summary>
    public static IReadOnlyList<TaskKey> Scan(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var found = new List<TaskKey>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in CandidatePattern().Matches(text))
        {
            if (!seen.Add(match.Value))
                continue;

            if (TaskKey.TryParse(match.Value, out var key))
                found.Add(key);
        }

        return found;
    }
}
