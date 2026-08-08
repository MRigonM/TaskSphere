using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

public interface IGitHubConnectionReadService
{
    Task<Result<GitHubConnectionDto>> GetConnectionAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes the installation and its repositories. Deliberately does not uninstall the
    /// App on GitHub, so the same installation id comes back on reconnect and Task 14 revives
    /// these rows (§0m). <c>ProjectRepositoryLink</c> rows are left untouched.
    /// </summary>
    Task<Result> DisconnectAsync(Guid companyId, CancellationToken cancellationToken = default);
}
