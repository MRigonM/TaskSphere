namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Counts for the sync summary. <c>KeysSeen</c> counts key mentions, deduplicated within each
/// record — three commits each naming TS-42 count three, one commit naming it twice counts one;
/// <c>KeysUnresolved</c> is how many of those routed nowhere — an unresolvable key is normal
/// traffic, not an error, so it is reported and never thrown.
/// </summary>
public sealed record TaskLinkResolution(int LinksCreated, int KeysSeen, int KeysUnresolved);

/// <summary>
/// Turns the mirror into <c>TaskLink</c> rows. It never sees GitHub JSON — that is the seam
/// the whole design rests on: when sub-project B2 lands, its webhook handler normalizes a
/// payload, upserts the mirror, and calls this, so the linking and authorization rules exist
/// exactly once.
/// <para>
/// It scans the company's whole live mirror on every run rather than a touched set. Inserting
/// a link is idempotent, so a full pass and an incremental pass produce the same rows — and a
/// full pass additionally picks up a key that became resolvable later, when a project key was
/// created after the commit naming it, or a repository was linked to a project whose commits
/// were already ingested.
/// </para>
/// </summary>
public interface IGitHubTaskLinkResolver
{
    Task<TaskLinkResolution> ResolveAsync(Guid companyId, CancellationToken cancellationToken = default);
}
