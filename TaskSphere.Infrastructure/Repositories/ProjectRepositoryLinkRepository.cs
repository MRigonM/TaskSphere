using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class ProjectRepositoryLinkRepository : GenericRepository<ProjectRepositoryLink, int>, IProjectRepositoryLinkRepository
{
    private readonly ApplicationDbContext _context;

    public ProjectRepositoryLinkRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IQueryable<ProjectRepositoryLink> GetByProject(Guid companyId, int projectId)
    {
        return _context.ProjectRepositoryLinks
            .Where(l => l.CompanyId == companyId && l.ProjectId == projectId);
    }

    public Task<ProjectRepositoryLink?> GetLinkAsync(Guid companyId, int projectId, int gitHubRepositoryId, CancellationToken cancellationToken = default)
    {
        return _context.ProjectRepositoryLinks
            .FirstOrDefaultAsync(
                l => l.CompanyId == companyId
                     && l.ProjectId == projectId
                     && l.GitHubRepositoryId == gitHubRepositoryId,
                cancellationToken);
    }
}
