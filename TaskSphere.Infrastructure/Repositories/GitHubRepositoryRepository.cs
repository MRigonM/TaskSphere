using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class GitHubRepositoryRepository : GenericRepository<GitHubRepository, int>, IGitHubRepositoryRepository
{
    private readonly ApplicationDbContext _context;

    public GitHubRepositoryRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IQueryable<GitHubRepository> GetByCompany(Guid companyId)
    {
        return _context.GitHubRepositories.Where(r => r.CompanyId == companyId);
    }

    public Task<GitHubRepository?> GetByGitHubIdIncludingDeletedAsync(long repositoryId, CancellationToken cancellationToken = default)
    {
        // See GitHubInstallationRepository.GetByGitHubIdIncludingDeletedAsync — bare entity,
        // no navigations, because filter suppression is query-wide.
        return _context.GitHubRepositories
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.RepositoryId == repositoryId, cancellationToken);
    }
}
