using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class GitHubBranchCommitRepository : GenericRepository<GitHubBranchCommit, int>, IGitHubBranchCommitRepository
{
    private readonly ApplicationDbContext _context;

    public GitHubBranchCommitRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IQueryable<GitHubBranchCommit> GetByCompany(Guid companyId)
    {
        return _context.GitHubBranchCommits.Where(bc => bc.CompanyId == companyId);
    }

    public Task<bool> ExistsForPairAsync(int gitHubBranchId, int gitHubCommitId, CancellationToken cancellationToken = default)
    {
        return _context.GitHubBranchCommits
            .AnyAsync(bc => bc.GitHubBranchId == gitHubBranchId && bc.GitHubCommitId == gitHubCommitId, cancellationToken);
    }
}
