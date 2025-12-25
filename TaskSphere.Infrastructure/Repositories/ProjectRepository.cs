using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class ProjectRepository : GenericRepository<Project, int>, IProjectRepository
{
    private readonly ApplicationDbContext _context;

    public ProjectRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public Task<Project?> GetCompanyProjectAsync(Guid companyId, int projectId, CancellationToken cancellationToken = default)
    {
        return _context.Projects
            .Include(p => p.Members)
            .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.CompanyId == companyId 
                                      && p.Id == projectId, cancellationToken);
    }

    public Task<bool> CompanyOwnsProjectAsync(Guid companyId, int projectId, CancellationToken cancellationToken = default)
    {
        return _context.Projects.AnyAsync(p => p.CompanyId == companyId 
                                               && p.Id == projectId, cancellationToken);
    }

    public IQueryable<Project> GetCompanyProjects(Guid companyId)
    {
        return _context.Projects.Where(p => p.CompanyId == companyId);
    }
}