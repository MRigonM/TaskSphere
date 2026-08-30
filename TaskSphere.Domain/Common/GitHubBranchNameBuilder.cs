using System.Text;

namespace TaskSphere.Domain.Common;

/// <summary>
/// Builds the branch name for a task: <c>TS-42/crud-for-product</c>.
/// <para>
/// Only the title is slugged. Slugging the whole string yields <c>ts-42-…</c>, and
/// <see cref="TaskKeyScanner"/> matches uppercase keys only — so a fully slugged name would
/// produce no link at all, and the feature would defeat the convention it exists to enforce.
/// </para>
/// <para>
/// The output charset is <c>[a-z0-9-]</c> plus the key and one <c>/</c>, which excludes every
/// character git forbids in a ref. Names that arrive from the client are a different matter:
/// see GitHubBranchNameValidator.
/// </para>
/// </summary>
public static class GitHubBranchNameBuilder
{
    private const int MaxSlugLength = 60;

    public static string Build(TaskKey key, string? title)
    {
        var slug = Slug(title);

        return slug.Length == 0 ? $"{key}/task" : $"{key}/{slug}";
    }

    private static string Slug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";

        var builder = new StringBuilder(title.Length);

        foreach (var c in title)
        {
            // Apostrophes are dropped rather than separated, so "user's" reads "users" and not
            // "user-s". Every other non-alphanumeric becomes one separator.
            if (c == '\'' || (int)c == 0x2019)
                continue;

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(c);
            else if (c is >= 'A' and <= 'Z')
                builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length <= MaxSlugLength)
            return slug;

        slug = slug[..MaxSlugLength];

        // Cut back to a word boundary when one is close, rather than ending mid-word.
        var lastDash = slug.LastIndexOf('-');

        return (lastDash >= MaxSlugLength - 10 ? slug[..lastDash] : slug).Trim('-');
    }
}
