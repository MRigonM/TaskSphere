namespace TaskSphere.Domain.Entities;

/// <summary>
/// A commit that was ahead of its repository's default branch on this branch <em>at the moment
/// the sync saw it</em>. A historical fact, never recomputed and never deleted: when the branch
/// merges, the commit stops being ahead but the row stays, which is what keeps an inherited
/// TaskLink alive past the merge.
/// <para>
/// Deliberately a join rather than a branch FK on <see cref="GitHubCommit"/>: a commit can be
/// ahead on two branches at once (a branch cut from another feature branch), and the commit row
/// itself must stay single and shared.
/// </para>
/// </summary>
public class GitHubBranchCommit : BaseEntity<int>
{
    public Guid CompanyId { get; set; }
    public int GitHubBranchId { get; set; }
    public GitHubBranch? Branch { get; set; }
    public int GitHubCommitId { get; set; }
    public GitHubCommit? Commit { get; set; }
}
