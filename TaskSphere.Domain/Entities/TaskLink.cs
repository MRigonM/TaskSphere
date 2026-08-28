namespace TaskSphere.Domain.Entities;

/// <summary>
/// Exactly one of the three record ids is set. Three nullable FKs rather than a polymorphic
/// (Kind, SourceId) pair, so referential integrity survives — and which FK is set already says
/// whether the link came from a commit, a branch or a pull request, so there is no MatchedVia
/// column. A join rather than a column on the record because one commit message may name
/// several keys.
/// </summary>
/// <remarks>
/// <see cref="ViaGitHubBranchId"/> distinguishes direct links (from commit names) from inherited
/// ones (from commit ahead-ness).
/// </remarks>
public class TaskLink : BaseEntity<int>
{
    public Guid CompanyId { get; set; }
    public int TaskId { get; set; }
    public Task? Task { get; set; }
    public int? GitHubCommitId { get; set; }
    public int? GitHubBranchId { get; set; }
    public int? GitHubPullRequestId { get; set; }

    /// <summary>
    /// Null when the record named the task itself. Set when the link was inherited: the commit
    /// sits ahead of the default branch on that branch, and never mentions the task at all.
    /// <para>
    /// Provenance only — it is NOT part of any unique index. (TaskId, GitHubCommitId) stays
    /// unique, so a commit that both names the task and sits on its branch is one row, and the
    /// resolver's pass order decides that it reads as direct.
    /// </para>
    /// </summary>
    public int? ViaGitHubBranchId { get; set; }
}
