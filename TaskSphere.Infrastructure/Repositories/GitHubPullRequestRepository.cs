using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class GitHubPullRequestRepository : GenericRepository<GitHubPullRequest, int>, IGitHubPullRequestRepository
{
    private readonly ApplicationDbContext _context;

    public GitHubPullRequestRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IQueryable<GitHubPullRequest> GetByCompany(Guid companyId)
    {
        return _context.GitHubPullRequests.Where(p => p.CompanyId == companyId);
    }

    public IQueryable<GitHubPullRequest> GetByRepository(Guid companyId, int gitHubRepositoryId)
    {
        return _context.GitHubPullRequests
            .Where(p => p.CompanyId == companyId && p.GitHubRepositoryId == gitHubRepositoryId);
    }

    public Task<GitHubPullRequest?> GetByNumberIncludingDeletedAsync(int gitHubRepositoryId, int number, CancellationToken cancellationToken = default)
    {
        return _context.GitHubPullRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.GitHubRepositoryId == gitHubRepositoryId && p.Number == number, cancellationToken);
    }
}
