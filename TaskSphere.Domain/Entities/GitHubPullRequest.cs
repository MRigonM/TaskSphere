using TaskSphere.Domain.Enums;

namespace TaskSphere.Domain.Entities;

/// <summary>
/// A state machine, which is why State is an indexable column rather than a field inside a
/// JSON payload. <c>GitHubUpdatedAtUtc</c> carries GitHub's updated_at — it cannot be called
/// UpdatedAtUtc, which ApplicationDbContext.SaveChangesAsync stamps on every modification.
/// </summary>
public class GitHubPullRequest : BaseEntity<int>
{
    public int GitHubRepositoryId { get; set; }
    public GitHubRepository? Repository { get; set; }
    public Guid CompanyId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public PullRequestState State { get; set; }
    public string AuthorLogin { get; set; } = "";
    public string HeadBranch { get; set; } = "";
    public DateTime OpenedAtUtc { get; set; }
    public DateTime GitHubUpdatedAtUtc { get; set; }
    public DateTime? MergedAtUtc { get; set; }
    public string HtmlUrl { get; set; } = "";

    /// <summary>
    /// TaskSphere's own column, not GitHub's — non-null means "this pull request has already
    /// been considered for a merge → Done transition", whether or not anything moved.
    /// It is how the transition is made idempotent without observing a state edge: the sync
    /// overwrites <see cref="State"/> on every pass, so "just became merged" is not
    /// observable after the write.
    /// The sync's upsert must leave this field alone.
    /// </summary>
    public DateTime? MergeTransitionAppliedAtUtc { get; set; }
}
