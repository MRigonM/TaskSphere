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
}
