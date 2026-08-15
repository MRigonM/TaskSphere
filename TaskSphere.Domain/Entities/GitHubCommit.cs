namespace TaskSphere.Domain.Entities;

/// <summary>
/// A commit in the mirror. Immutable once ingested — a commit's content never changes, only
/// which branches reach it. <c>CommittedAtUtc</c> is GitHub's timestamp, deliberately not
/// named CreatedAtUtc, which ApplicationDbContext.SaveChangesAsync would overwrite.
/// </summary>
public class GitHubCommit : BaseEntity<int>
{
    public int GitHubRepositoryId { get; set; }
    public GitHubRepository? Repository { get; set; }
    public Guid CompanyId { get; set; }
    public string Sha { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>The git author, always present.</summary>
    public string AuthorName { get; set; } = "";
    /// <summary>The GitHub account, absent when GitHub cannot match the commit to a user.</summary>
    public string? AuthorLogin { get; set; }
    public DateTime CommittedAtUtc { get; set; }
    public string HtmlUrl { get; set; } = "";
}
