using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// The §0l verification chain. GitHub documents that the <c>installation_id</c> on the setup
/// redirect can be spoofed, and the install <c>state</c> proves nothing about it — the state is
/// minted before the redirect and carries no installation id. So before anything is persisted,
/// the callback exchanges the <c>code</c> for a <b>user</b> access token and asks GitHub which
/// installations that user actually has.
/// <para>
/// The user token is used once and discarded: never cached, never persisted. It is a different
/// credential from the installation token, and confusing the two is how tenant boundaries leak.
/// </para>
/// </summary>
/// <summary>
/// An installation as the authenticating user sees it. The account and repository selection
/// come from this same response, so verifying an installation and learning what it is take one
/// call rather than a second round-trip on the App JWT.
/// </summary>
public sealed record GitHubUserInstallation(
    long Id,
    string AccountLogin,
    string AccountType,
    string RepositorySelection);

public interface IGitHubUserAuthService
{
    Task<Result<string>> ExchangeCodeForUserTokenAsync(string code, CancellationToken cancellationToken = default);

    Task<Result<bool>> UserHasInstallationAsync(string userToken, long installationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The verification of <see cref="UserHasInstallationAsync"/> plus the metadata needed to
    /// persist the mapping. Returns a successful result holding <c>null</c> when the user does
    /// not have that installation — absence is an answer, not an error.
    /// </summary>
    Task<Result<GitHubUserInstallation?>> FindUserInstallationAsync(string userToken, long installationId, CancellationToken cancellationToken = default);
}
