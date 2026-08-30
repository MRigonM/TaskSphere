using TaskSphere.Domain.Enums;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// GitHub reports repository selection as the string "all" or "selected". §0r: an unrecognised
/// value is a failure, never a silent default — <see cref="RepositorySelection.Selected"/> is 0,
/// so defaulting would quietly claim "only selected repositories" about an install that can
/// actually see every one of them.
/// <para>
/// Shared because two endpoints report it: /user/installations on the callback, and
/// /installation/repositories on every sync.
/// </para>
/// </summary>
internal static class RepositorySelectionParser
{
    public static bool TryParse(string? value, out RepositorySelection selection)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "all":
                selection = RepositorySelection.All;
                return true;
            case "selected":
                selection = RepositorySelection.Selected;
                return true;
            default:
                selection = RepositorySelection.Selected;
                return false;
        }
    }
}
