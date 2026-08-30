using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

public interface IGitHubConnectionService
{
    /// <summary>
    /// Builds the GitHub App installation URL for this company, carrying a protected state.
    /// </summary>
    Result<GitHubInstallUrlDto> GetInstallUrl(Guid companyId, string userId);

    /// <summary>
    /// Handles the post-install callback. Order of operations is mandatory: unprotect the
    /// state, assert it belongs to the calling company, verify the installation against the
    /// user's own installations (§0l), and only then persist the mapping.
    /// </summary>
    Task<Result<GitHubInstallationDto>> ConnectAsync(Guid companyId, string userId, ConnectGitHubDto dto, CancellationToken cancellationToken = default);
}
