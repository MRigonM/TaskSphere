using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class GitHubCommitRepository : GenericRepository<GitHubCommit, int>, IGitHubCommitRepository
{
    private readonly ApplicationDbContext _context;

    public GitHubCommitRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IQueryable<GitHubCommit> GetByCompany(Guid companyId)
    {
        return _context.GitHubCommits.Where(c => c.CompanyId == companyId);
    }

    public IQueryable<GitHubCommit> GetByRepository(Guid companyId, int gitHubRepositoryId)
    {
        return _context.GitHubCommits
            .Where(c => c.CompanyId == companyId && c.GitHubRepositoryId == gitHubRepositoryId);
    }

    public Task<GitHubCommit?> GetByShaIncludingDeletedAsync(int gitHubRepositoryId, string sha, CancellationToken cancellationToken = default)
    {
        // See GitHubRepositoryRepository.GetByGitHubIdIncludingDeletedAsync — bare entity, no
        // navigations, because filter suppression is query-wide.
        return _context.GitHubCommits
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.GitHubRepositoryId == gitHubRepositoryId && c.Sha == sha, cancellationToken);
    }
}
