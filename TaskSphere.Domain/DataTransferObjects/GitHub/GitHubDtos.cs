using TaskSphere.Domain.Enums;

namespace TaskSphere.Domain.DataTransferObjects.GitHub;

/// <summary>
/// What the Angular callback route POSTs back. <c>InstallationId</c> is untrusted input (§0l) —
/// it is verified against the authenticating user's own installations before anything is
/// persisted.
/// </summary>
public record ConnectGitHubDto(long InstallationId, string State, string Code);

/// <summary>
/// The URL the SPA navigates to. Returned as JSON rather than a 302 because the caller is an
/// XHR, which cannot follow a redirect to a third-party host (§0g).
/// </summary>
public record GitHubInstallUrlDto(string Url);

public record GitHubInstallationDto(
    int Id,
    long InstallationId,
    string AccountLogin,
    string AccountType,
    RepositorySelection RepositorySelection,
    bool IsSuspended);

public record GitHubRepositoryDto(
    int Id,
    long RepositoryId,
    string FullName,
    string DefaultBranch,
    bool IsPrivate);

/// <summary>
/// The company's connection state. A null <c>Installation</c> is a successful "not connected"
/// answer, not an error — the Connect button is shown whenever nothing is mapped (§0q).
/// </summary>
public record GitHubConnectionDto(
    GitHubInstallationDto? Installation,
    IReadOnlyList<GitHubRepositoryDto> Repositories);

/// <summary>
/// A project's linked repositories. Links whose repository row is soft-deleted are filtered out
/// rather than rendered, and surface only as <c>UnavailableCount</c> — the link row itself is
/// kept so history does not lie.
/// </summary>
public record ProjectRepositoriesDto(
    IReadOnlyList<ProjectRepositoryLinkDto> Links,
    int UnavailableCount);

public record LinkRepositoryDto(int RepositoryId);

public record ProjectRepositoryLinkDto(
    int Id,
    int ProjectId,
    int GitHubRepositoryId,
    string FullName,
    string LinkedByUserId);
