using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class GitHubInstallationRepository : GenericRepository<GitHubInstallation, int>, IGitHubInstallationRepository
{
    private readonly ApplicationDbContext _context;

    public GitHubInstallationRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public Task<GitHubInstallation?> GetByCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        return _context.GitHubInstallations
            .FirstOrDefaultAsync(i => i.CompanyId == companyId, cancellationToken);
    }

    public Task<GitHubInstallation?> GetByGitHubIdIncludingDeletedAsync(long installationId, CancellationToken cancellationToken = default)
    {
        // No Include: IgnoreQueryFilters is query-wide, so any navigation loaded here would
        // bypass soft-delete on the related entity too. Callers needing related data run a
        // second, filtered query.
        return _context.GitHubInstallations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.InstallationId == installationId, cancellationToken);
    }
}
