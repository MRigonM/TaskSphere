using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Infrastructure.Services;

public class GitHubConnectionReadService : IGitHubConnectionReadService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IGitHubRepositorySyncService _syncService;

    public GitHubConnectionReadService(IUnitOfWork unitOfWork, IMapper mapper, IGitHubRepositorySyncService syncService)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _syncService = syncService;
    }

    public async Task<Result<GitHubConnectionDto>> GetConnectionAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        // Filtered on purpose: a disconnected installation must read as "not connected" so the
        // Connect button comes back (§0q). The revive path uses its own unfiltered lookup.
        var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
        {
            return Result<GitHubConnectionDto>.Success(
                new GitHubConnectionDto(null, Array.Empty<GitHubRepositoryDto>()));
        }

        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => r.GitHubInstallationId == installation.Id)
            .OrderBy(r => r.FullName)
            .ToListAsync(cancellationToken);

        return Result<GitHubConnectionDto>.Success(new GitHubConnectionDto(
            _mapper.Map<GitHubInstallationDto>(installation),
            _mapper.Map<List<GitHubRepositoryDto>>(repositories)));
    }

    public async Task<Result<GitHubConnectionDto>> RefreshRepositoriesAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
            return Result<GitHubConnectionDto>.Failure(EntityError.NotFound("GitHub connection"));

        var sync = await _syncService.SyncAsync(installation, cancellationToken);

        // Propagated rather than swallowed: falling through to GetConnectionAsync would answer
        // a failed refresh with the stale list and no indication anything went wrong.
        // The whole list, not Errors[0]: ApiBaseController.MapErrors scans every error to choose
        // the status code, so keeping only the first can downgrade a 403 to a 400.
        if (!sync.IsSuccess)
            return Result<GitHubConnectionDto>.Failure([.. sync.Errors]);

        var refreshed = await GetConnectionAsync(companyId, cancellationToken);

        // GetConnectionAsync answers "not connected" as an empty SUCCESS, which is the opposite
        // of this method's contract. The two disagree whenever the installation goes away while
        // the sync is in flight — a disconnect in another tab during a round-trip to GitHub.
        // Without this the caller is told, successfully, that it has no repositories.
        if (refreshed.IsSuccess && refreshed.Value!.Installation is null)
            return Result<GitHubConnectionDto>.Failure(EntityError.NotFound("GitHub connection"));

        return refreshed;
    }

    public async Task<Result> DisconnectAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
            return Result.Failure(EntityError.NotFound("GitHub connection"));

        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => r.GitHubInstallationId == installation.Id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var repository in repositories)
        {
            repository.IsDeleted = true;
            repository.DeletedAt = now;
            await _unitOfWork.GitHubRepositories.Update(repository, cancellationToken);
        }

        installation.IsDeleted = true;
        installation.DeletedAt = now;
        await _unitOfWork.GitHubInstallations.Update(installation, cancellationToken);

        // ProjectRepositoryLink rows are deliberately untouched: same principle as immutable
        // task keys, and reconnect revives the repositories underneath them.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
