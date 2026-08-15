namespace TaskSphere.Domain.Enums;

/// <summary>
/// Stored as an int: internal machine state, never rendered raw, the same call
/// <c>RepositorySelection</c> made. GitHub reports open/closed plus a merged_at timestamp;
/// Merged is derived during ingestion, not sent by GitHub as a state.
/// </summary>
public enum PullRequestState
{
    Open = 0,
    Closed = 1,
    Merged = 2,
}
