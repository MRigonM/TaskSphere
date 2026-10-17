using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;

namespace TaskSphere.Mcp;

/// <summary>
/// Claude never sees repository ids, so create_branch takes a name. A name with a slash is a full
/// owner/name; without one it is the part after the owner. Every refusal lists the linked
/// repositories, so Claude can retry without a separate read tool.
/// </summary>
public static class McpRepositoryMatch
{
    public static Result<BranchRepositoryOptionDto> Resolve(
        string? name, IReadOnlyList<BranchRepositoryOptionDto> linked, string taskKey)
    {
        var listed = $"Linked repositories: {string.Join(", ", linked.Select(r => r.FullName))}.";
        var wanted = name?.Trim();

        if (string.IsNullOrEmpty(wanted))
        {
            return linked.Count == 1
                ? Result<BranchRepositoryOptionDto>.Success(linked[0])
                : Fail($"{taskKey}'s project links more than one repository; pass repository. {listed}");
        }

        var matches = linked
            .Where(r => wanted.Contains('/')
                ? string.Equals(r.FullName, wanted, StringComparison.OrdinalIgnoreCase)
                : string.Equals(r.FullName[(r.FullName.IndexOf('/') + 1)..], wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            1 => Result<BranchRepositoryOptionDto>.Success(matches[0]),
            0 => Fail($"No repository named {wanted} is linked to {taskKey}'s project. {listed}"),
            _ => Fail($"{wanted} matches more than one linked repository; pass owner/name. {listed}"),
        };
    }

    private static Result<BranchRepositoryOptionDto> Fail(string message) =>
        Result<BranchRepositoryOptionDto>.Failure(new Error("Validation.Repository", message));
}
