using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Counts for the sync summary. <c>Transitioned</c> is tasks actually moved to Done;
/// <c>Skipped</c> is pull requests considered that moved nothing — no key, an unresolvable
/// key, the project opted out, or the task was Blocked or already Done; <c>Failed</c> is pull
/// requests that threw. A failure is a count, not an error: it does not abort the pass.
/// </summary>
public sealed record MergeTransitionResult(int Transitioned, int Skipped, int Failed);

/// <summary>
/// Moves a task to Done when a pull request whose HEAD BRANCH names its key is merged.
/// <para>
/// It does not observe a state change. The sync overwrites PullRequest.State on every pass, so
/// "just became merged" is unobservable after the write; instead a pull request is eligible
/// when <c>State == Merged</c> and its <c>MergeTransitionAppliedAtUtc</c> marker is null, and
/// the marker is stamped unconditionally afterwards.
/// </para>
/// <para>
/// The key comes from the head branch only — never the title or body. A pull request that
/// merely mentions TS-42 does not move TS-42.
/// </para>
/// </summary>
public interface IMergeTransitionService
{
    /// <param name="repositoryIds">
    /// Restricts the pass to pull requests in these repositories. Null means the whole company,
    /// which is what the admin-triggered sync passes.
    /// <para>
    /// Deliberately repositories and not projects: a repository can be linked to several
    /// projects, so a head branch can name keys outside a project filter. Skipping those keys
    /// while stamping the marker strands them forever; skipping them without stamping leaves
    /// the pull request eligible, and a later pass would re-apply the transition over a human
    /// who moved the task back. Filtering on repositories keeps every considered pull request
    /// considered in full.
    /// </para>
    /// </param>
    Task<Result<MergeTransitionResult>> ApplyAsync(
        Guid companyId,
        string? actorUsername,
        IReadOnlyCollection<int>? repositoryIds = null,
        CancellationToken cancellationToken = default);
}
