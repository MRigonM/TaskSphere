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

    /// <summary>
    /// When the activity sync last completed with at least one repository synced. Null until
    /// the first run. Surfaced on the activity read so the panel says when it last looked
    /// rather than implying it is live — the honest answer to pull-based ingestion.
    /// </summary>
    public DateTime? ActivitySyncedAtUtc { get; set; }

    public ICollection<GitHubRepository> Repositories { get; set; } = new List<GitHubRepository>();
}
