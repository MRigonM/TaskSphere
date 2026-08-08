namespace TaskSphere.Domain.Entities;

// Named ProjectRepositoryLink, not ProjectRepository: that name is taken by
// TaskSphere.Infrastructure.Repositories.ProjectRepository.
public class ProjectRepositoryLink : BaseEntity<int>
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int GitHubRepositoryId { get; set; }
    public GitHubRepository? Repository { get; set; }
    public Guid CompanyId { get; set; }
    public string LinkedByUserId { get; set; } = "";
}
