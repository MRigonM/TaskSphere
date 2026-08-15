namespace TaskSphere.Domain.Entities;

/// <summary>
/// Exactly one of the three record ids is set. Three nullable FKs rather than a polymorphic
/// (Kind, SourceId) pair, so referential integrity survives — and which FK is set already says
/// whether the link came from a commit, a branch or a pull request, so there is no MatchedVia
/// column. A join rather than a column on the record because one commit message may name
/// several keys.
/// </summary>
public class TaskLink : BaseEntity<int>
{
    public Guid CompanyId { get; set; }
    public int TaskId { get; set; }
    public Task? Task { get; set; }
    public int? GitHubCommitId { get; set; }
    public int? GitHubBranchId { get; set; }
    public int? GitHubPullRequestId { get; set; }
}
