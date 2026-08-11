namespace TaskSphere.Domain.DataTransferObjects.GitHub;

public record LinkedProjectDto(int Id, string Key, string Name);

/// <summary>
/// One row of the repository-links table: a live repository and every project linked to it.
/// Deliberately omits RepositoryId, IsPrivate and DefaultBranch — the Repositories list above
/// this table on the same screen already renders those.
/// </summary>
public record RepositoryLinksDto(int Id, string FullName, List<LinkedProjectDto> Projects);

/// <summary>
/// Links pointing at a repository that is no longer live, grouped by project. A count and a
/// project key, never a repository name: reading the name of a soft-deleted repository would
/// need IgnoreQueryFilters, and the name is not worth suppressing a tenancy filter for.
/// </summary>
public record UnavailableProjectLinksDto(int ProjectId, string ProjectKey, int Count);

public record CompanyRepositoryLinksDto(
    List<RepositoryLinksDto> Repositories,
    List<UnavailableProjectLinksDto> Unavailable);
