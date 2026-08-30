namespace TaskSphere.Domain.Entities;

/// <summary>
/// A moving pointer, not a record of history: <c>HeadSha</c> is overwritten on every sync.
/// A branch absent from a sync response is soft-deleted rather than removed, so its TaskLink
/// survives and the panel can say the branch is gone instead of silently losing it.
/// </summary>
public class GitHubBranch : BaseEntity<int>
{
    public int GitHubRepositoryId { get; set; }
    public GitHubRepository? Repository { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = "";
    public string HeadSha { get; set; } = "";
}
