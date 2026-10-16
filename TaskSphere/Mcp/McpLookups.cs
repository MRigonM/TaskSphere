using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Task;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Mcp;

/// <summary>Key → entity resolution and display helpers shared by the tool classes.</summary>
public sealed class McpLookups
{
    private readonly IReadOnlyUnitOfWork _unitOfWork;
    private readonly IProjectService _projects;

    public McpLookups(IReadOnlyUnitOfWork unitOfWork, IProjectService projects)
    {
        _unitOfWork = unitOfWork;
        _projects = projects;
    }

    public Task<Project?> ProjectAsync(AgentCaller caller, string key, CancellationToken ct) =>
        _unitOfWork.Projects.GetByKeyAsync(caller.CompanyId, (key ?? "").Trim().ToUpperInvariant(), ct);

    /// <summary>UserId → name for the project's members; empty when the caller cannot see them.</summary>
    public async Task<IReadOnlyDictionary<string, string>> MemberNamesAsync(AgentCaller caller, int projectId, CancellationToken ct)
    {
        var members = await _projects.GetMembersAsync(caller.CompanyId, projectId, caller.UserId, false, ct);
        return members.IsSuccess
            ? members.Value!.ToDictionary(m => m.UserId, m => m.UserName)
            : new Dictionary<string, string>();
    }

    public static string TaskLine(TaskDto t, IReadOnlyDictionary<string, string> names)
    {
        var priority = t.Priority is null ? "" : $" ({t.Priority})";
        var assignee = t.AssigneeUserId is null
            ? "unassigned"
            : names.TryGetValue(t.AssigneeUserId, out var n) ? n : "someone outside the project";
        return $"{t.Key} [{t.Status}]{priority} {t.Title} — {assignee}";
    }
}
