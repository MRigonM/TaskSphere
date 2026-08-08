using TaskSphere.Domain.Enums;

namespace TaskSphere.Domain.Entities;

public class GitHubInstallation : BaseEntity<int>
{
    public long InstallationId { get; set; }
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string AccountLogin { get; set; } = "";
    public string AccountType { get; set; } = "";
    public RepositorySelection RepositorySelection { get; set; }
    public bool IsSuspended { get; set; }
    public ICollection<GitHubRepository> Repositories { get; set; } = new List<GitHubRepository>();
}
