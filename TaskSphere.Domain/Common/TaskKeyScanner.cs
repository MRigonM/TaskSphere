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
    // Lookbehind, not \b: on ASCII letters and digits the two agree (\b also reads "XTS-42" as
    // one key for project XTS). They diverge on everything else \b counts as a word character —
    // "_", non-ASCII letters and digits, combining marks. "fix_TS-42" is a legal branch name,
    // so the lookbehind deliberately admits it where \b would not.
    // The trailing (?![0-9]) is not about TS-421 — greedy matching already takes 421 whole.
    // It makes a ten-digit run no key at all rather than a nine-digit key with its tail
    // chopped off: "TS-1234567890" scans as nothing, not as TS-123456789.
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
