using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class GitHubBranchRepository : GenericRepository<GitHubBranch, int>, IGitHubBranchRepository
{
    private readonly ApplicationDbContext _context;

    public GitHubBranchRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IQueryable<GitHubBranch> GetByCompany(Guid companyId)
    {
        return _context.GitHubBranches.Where(b => b.CompanyId == companyId);
    }

    public IQueryable<GitHubBranch> GetByRepository(Guid companyId, int gitHubRepositoryId)
    {
        return _context.GitHubBranches
            .Where(b => b.CompanyId == companyId && b.GitHubRepositoryId == gitHubRepositoryId);
    }

    public Task<GitHubBranch?> GetByNameIncludingDeletedAsync(int gitHubRepositoryId, string name, CancellationToken cancellationToken = default)
    {
        return _context.GitHubBranches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.GitHubRepositoryId == gitHubRepositoryId && b.Name == name, cancellationToken);
    }

    public IQueryable<GitHubBranch> GetByCompanyIncludingDeleted(Guid companyId)
    {
        return _context.GitHubBranches
            .IgnoreQueryFilters()
            .Where(b => b.CompanyId == companyId);
    }
}
