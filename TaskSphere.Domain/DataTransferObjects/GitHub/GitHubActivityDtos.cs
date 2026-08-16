using TaskSphere.Domain.Enums;

namespace TaskSphere.Domain.DataTransferObjects.GitHub;

/// <summary>
/// One repository that could not be synced. Partial success is a result, not an error: a
/// repository that fails does not abort the run, and the endpoint still answers 200 with the
/// summary.
/// </summary>
public record SyncFailureDto(string RepositoryFullName, string Reason);

public record SyncActivityResultDto(
    int RepositoriesSynced,
    int Commits,
    int Branches,
    int PullRequests,
    int LinksCreated,
    IReadOnlyList<SyncFailureDto> Failures);

public record TaskCommitDto(
    string Sha,
    string ShortSha,
    string Message,
    string AuthorName,
    string? AuthorLogin,
    DateTime CommittedAtUtc,
    string HtmlUrl,
    string RepositoryFullName);

/// <summary>
/// <c>IsDeleted</c> is rendered, not filtered: a branch that GitHub no longer reports is shown
/// with a marker rather than disappearing from a task's history.
/// </summary>
public record TaskBranchDto(
    string Name,
    string HeadSha,
    bool IsDeleted,
    string RepositoryFullName);

public record TaskPullRequestDto(
    int Number,
    string Title,
    PullRequestState State,
    string AuthorLogin,
    DateTime OpenedAtUtc,
    DateTime? MergedAtUtc,
    string HtmlUrl,
    string RepositoryFullName);

/// <summary>
/// <c>LastSyncedAtUtc</c> is null until an activity sync has run. The panel says when it last
/// looked rather than implying it is live — the honest answer to pull-based ingestion.
/// </summary>
public record TaskGitHubActivityDto(
    IReadOnlyList<TaskCommitDto> Commits,
    IReadOnlyList<TaskBranchDto> Branches,
    IReadOnlyList<TaskPullRequestDto> PullRequests,
    DateTime? LastSyncedAtUtc);
