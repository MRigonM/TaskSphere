namespace TaskSphere.Domain.DataTransferObjects.GitHub;

/// <summary>What the dialog needs before it can ask anything: the name it proposes, the key it
/// must keep, and the repositories the project actually links.</summary>
public sealed record BranchSuggestionDto(
    string TaskKey,
    string SuggestedName,
    IReadOnlyList<BranchRepositoryOptionDto> Repositories);

public sealed record BranchRepositoryOptionDto(int Id, string FullName, string DefaultBranch);

/// <summary>RepositoryId is optional: a project that links exactly one repository does not ask.</summary>
public sealed record CreateBranchDto(int? RepositoryId, string Name);

/// <summary>AlreadyExisted is not an error — the branch is there, which is what was asked for.</summary>
public sealed record CreatedBranchDto(
    int Id,
    string Name,
    string HeadSha,
    string HtmlUrl,
    bool AlreadyExisted);
