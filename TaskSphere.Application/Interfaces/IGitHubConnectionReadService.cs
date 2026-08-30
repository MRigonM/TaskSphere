using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Application.Interfaces;

public interface IGitHubConnectionReadService
{
    Task<Result<GitHubConnectionDto>> GetConnectionAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads the installation's repository list from GitHub on demand and returns the
    /// refreshed connection. Unlike the install callback this establishes nothing: the
    /// installation is resolved from <paramref name="companyId"/>, so no caller-supplied
    /// installation id is trusted.
    /// <para>
    /// A company with no live installation is a failure, not an empty success — an empty
    /// success renders as "no repositories", which is a different claim.
    /// </para>
    /// </summary>
    Task<Result<GitHubConnectionDto>> RefreshRepositoriesAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes the installation and its repositories. Deliberately does not uninstall the
    /// App on GitHub, so the same installation id comes back on reconnect and Task 14 revives
    /// these rows (§0m). <c>ProjectRepositoryLink</c> rows are left untouched.
    /// </summary>
    Task<Result> DisconnectAsync(Guid companyId, CancellationToken cancellationToken = default);
}
