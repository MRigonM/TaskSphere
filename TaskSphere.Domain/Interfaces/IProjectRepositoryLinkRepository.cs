using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IProjectRepositoryLinkRepository : IGenericRepository<ProjectRepositoryLink, int>
{
    IQueryable<ProjectRepositoryLink> GetByCompany(Guid companyId);

    IQueryable<ProjectRepositoryLink> GetByProject(Guid companyId, int projectId);

    Task<ProjectRepositoryLink?> GetLinkAsync(Guid companyId, int projectId, int gitHubRepositoryId, CancellationToken cancellationToken = default);
}
