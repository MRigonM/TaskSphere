using System.Text.RegularExpressions;

namespace TaskSphere.Domain.Common;

/// <summary>
/// Validates a branch name that arrived from a client. <see cref="GitHubBranchNameBuilder"/>
/// output is safe by construction; an edited name is not.
/// </summary>
public static partial class GitHubBranchNameValidator
{
    private const int MaxLength = 200;

    // An allowlist rather than a denylist of git's forbidden characters: everything git rejects
    // (space, ~, ^, :, ?, *, [, \, control characters) is simply absent from this set.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]*\z")]
    private static partial Regex AllowedPattern();

    public static bool IsValidRefName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxLength)
            return false;

        if (!AllowedPattern().IsMatch(name))
            return false;

        if (name.Contains("..") || name.Contains("//") || name.Contains("@{"))
            return false;

        if (name.EndsWith('/') || name.EndsWith('.'))
            return false;

        foreach (var part in name.Split('/'))
        {
            if (part.Length == 0 || part.StartsWith('.') || part.EndsWith(".lock", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="name"/> names <paramref name="key"/> — this task, not merely
    /// some task. Scanning for any key would let "TS-42/see-also-TS-7" open a branch filed
    /// against TS-7.
    /// </summary>
    public static bool NamesTask(string? name, TaskKey key)
        => name is not null && TaskKeyScanner.Scan(name).Contains(key);
}
