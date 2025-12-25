using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IProjectRepository : IGenericRepository<Project, int>
{
    Task<Project?> GetCompanyProjectAsync(Guid companyId, int projectId, CancellationToken cancellationToken = default);
    Task<bool> CompanyOwnsProjectAsync(Guid companyId, int projectId, CancellationToken cancellationToken = default);
    IQueryable<Project> GetCompanyProjects(Guid companyId);
}