using AutoMapper;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Configuration;

namespace TaskSphere.Infrastructure.Services;

public class GitHubConnectionService : IGitHubConnectionService
{
    private readonly GitHubAppOptions _options;
    private readonly IGitHubInstallStateService _stateService;
    private readonly IGitHubUserAuthService _userAuthService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGitHubRepositorySyncService _syncService;
    private readonly IMapper _mapper;

    public GitHubConnectionService(
        IOptions<GitHubAppOptions> options,
        IGitHubInstallStateService stateService,
        IGitHubUserAuthService userAuthService,
        IUnitOfWork unitOfWork,
        IGitHubRepositorySyncService syncService,
        IMapper mapper)
    {
        _options = options.Value;
        _stateService = stateService;
        _userAuthService = userAuthService;
        _unitOfWork = unitOfWork;
        _syncService = syncService;
        _mapper = mapper;
    }

    public Result<GitHubInstallUrlDto> GetInstallUrl(Guid companyId, string userId)
    {
        if (string.IsNullOrWhiteSpace(_options.AppSlug))
        {
            return Result<GitHubInstallUrlDto>.Failure(new Error(
                "GitHub.NotConfigured",
                "The GitHub App slug is not configured."));
        }

        var state = _stateService.Protect(companyId, userId);

        var url = $"https://github.com/apps/{_options.AppSlug}/installations/new" +
                  $"?state={Uri.EscapeDataString(state)}";

        return Result<GitHubInstallUrlDto>.Success(new GitHubInstallUrlDto(url));
    }

    public async Task<Result<GitHubInstallationDto>> ConnectAsync(
        Guid companyId,
        string userId,
        ConnectGitHubDto dto,
        CancellationToken cancellationToken = default)
    {
        // 1. The state proves the flow started here. Checked first because the OAuth code is
        //    single-use — spending it on a request that is going to be rejected wastes it.
        var state = _stateService.Unprotect(dto.State);

        if (!state.IsSuccess)
            return Result<GitHubInstallationDto>.Failure(state.Errors[0]);

        // 2. The state's company against the caller's. This is the CSRF binding (§0n).
        if (state.Value!.CompanyId != companyId)
            return Forbidden("The install state does not belong to this company.");

        // 3. §0l. Nothing above this point proves anything about dto.InstallationId — the state
        //    was minted before the redirect and carries no installation id. Without this check
        //    an authenticated user can POST a stranger's installation id and have TaskSphere
        //    mint access tokens for it.
        var userToken = await _userAuthService.ExchangeCodeForUserTokenAsync(dto.Code, cancellationToken);

        if (!userToken.IsSuccess)
            return Forbidden("GitHub could not verify the installation for this user.");

        var verified = await _userAuthService.FindUserInstallationAsync(userToken.Value!, dto.InstallationId, cancellationToken);

        if (!verified.IsSuccess)
            return Forbidden("GitHub could not verify the installation for this user.");

        if (verified.Value is null)
            return Forbidden("That installation does not belong to the authenticated GitHub user.");

        if (!RepositorySelectionParser.TryParse(verified.Value.RepositorySelection, out var repositorySelection))
        {
            return Result<GitHubInstallationDto>.Failure(new Error(
                "GitHub.UnknownRepositorySelection",
                $"GitHub reported an unrecognised repository selection '{verified.Value.RepositorySelection}'."));
        }

        // 4. IgnoreQueryFilters, because IX_GitHubInstallations_InstallationId is unfiltered
        //    (§0m). A filtered check would miss a disconnected row and collide on insert.
        var existing = await _unitOfWork.GitHubInstallations
            .GetByGitHubIdIncludingDeletedAsync(dto.InstallationId, cancellationToken);

        GitHubInstallation installation;

        if (existing is null)
        {
            installation = new GitHubInstallation
            {
                InstallationId = dto.InstallationId,
                CompanyId = companyId,
                AccountLogin = verified.Value.AccountLogin,
                AccountType = verified.Value.AccountType,
                RepositorySelection = repositorySelection,
            };

            await _unitOfWork.GitHubInstallations.AddAsync(installation, cancellationToken);
        }
        else if (existing.CompanyId != companyId)
        {
            // Deliberately does not promise permanence: a genuine GitHub-side uninstall issues
            // a new installation id, after which another company can connect the org normally.
            return Result<GitHubInstallationDto>.Failure(EntityError.Conflict(
                $"The GitHub organization '{existing.AccountLogin}' is already connected to another company. " +
                "It can only be connected here after the app is uninstalled on GitHub and reinstalled."));
        }
        else
        {
            // Revive rather than insert. Same row, same id, cleared tombstone — SaveChangesAsync
            // re-stamps UpdatedAtUtc.
            installation = existing;
            installation.IsDeleted = false;
            installation.DeletedAt = null;
            installation.AccountLogin = verified.Value.AccountLogin;
            installation.AccountType = verified.Value.AccountType;
            installation.RepositorySelection = repositorySelection;
            installation.IsSuspended = false;

            await _unitOfWork.GitHubInstallations.Update(installation, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 5. Sync the repository list. A failure here leaves the mapping in place on purpose:
        //    connecting succeeded, and Connect is idempotent, so a retry re-syncs rather than
        //    re-creating anything. The caller still sees the failure instead of an empty list
        //    with no explanation.
        var sync = await _syncService.SyncAsync(installation, cancellationToken);

        if (!sync.IsSuccess)
            return Result<GitHubInstallationDto>.Failure(sync.Errors[0]);

        return Result<GitHubInstallationDto>.Success(_mapper.Map<GitHubInstallationDto>(installation));
    }

    // Every §0l rejection answers the same way. Telling a caller which step failed tells an
    // attacker which half of the check to work around.
    private static Result<GitHubInstallationDto> Forbidden(string message)
        => Result<GitHubInstallationDto>.Failure(new Error("Auth.Forbidden", message));
}
