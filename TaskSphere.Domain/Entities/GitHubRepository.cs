namespace TaskSphere.Domain.Entities;

public class GitHubRepository : BaseEntity<int>
{
    public long RepositoryId { get; set; }
    public int GitHubInstallationId { get; set; }
    public GitHubInstallation? Installation { get; set; }
    public Guid CompanyId { get; set; }
    public string FullName { get; set; } = "";
    public string DefaultBranch { get; set; } = "";
    public bool IsPrivate { get; set; }

    /// <summary>
    /// TaskSphere's own column, not GitHub's — when the project-scoped refresh last pulled this
    /// repository's pull requests. Drives a per-repository cooldown, so several boards opening
    /// at once cost one GitHub call rather than one each.
    /// The repository upsert must leave this field alone.
    /// </summary>
    public DateTime? PullRequestsRefreshedAtUtc { get; set; }

    /// <summary>
    /// TaskSphere's own column, not GitHub's — when a task-scoped refresh last pulled this
    /// repository's commits. A separate stamp from PullRequestsRefreshedAtUtc because the commits
    /// pass costs one listing per branch and carries a much longer cooldown.
    /// The repository upsert must leave this field alone.
    /// </summary>
    public DateTime? CommitsRefreshedAtUtc { get; set; }
}
