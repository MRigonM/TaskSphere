using System.ComponentModel;
using System.Security.Claims;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Task;

namespace TaskSphere.Mcp;

[McpServerToolType]
public sealed class ProjectAndTaskReadTools
{
    private readonly IProjectService _projects;
    private readonly ITaskService _tasks;
    private readonly ISprintService _sprints;
    private readonly McpLookups _lookups;

    public ProjectAndTaskReadTools(IProjectService projects, ITaskService tasks, ISprintService sprints, McpLookups lookups)
    {
        _projects = projects;
        _tasks = tasks;
        _sprints = sprints;
        _lookups = lookups;
    }

    [McpServerTool(Name = "list_projects"), Description("Lists the TaskSphere projects you are a member of, as KEY — Name.")]
    public async Task<CallToolResult> ListProjects(ClaimsPrincipal? user, CancellationToken ct)
    {
        var caller = AgentCaller.From(user);
        var result = await _projects.GetMembersProjects(caller.CompanyId, caller.UserId, ct);
        if (!result.IsSuccess) return ToolResult.Error(result.Errors);

        var lines = result.Value!.Select(p => $"{p.Key} — {p.Name}").ToList();
        return ToolResult.Ok(lines.Count == 0 ? "You are not a member of any project yet." : string.Join("\n", lines));
    }

    [McpServerTool(Name = "list_tasks"), Description("Lists a project's tasks as KEY [Status] (Priority) Title — Assignee.")]
    public async Task<CallToolResult> ListTasks(
        ClaimsPrincipal? user,
        [Description("Project key, for example TS")] string project,
        [Description("Only tasks in this status: Open, InProgress, Blocked or Done")] string? status = null,
        [Description("all (default), backlog, or active-sprint")] string? scope = null,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var p = await _lookups.ProjectAsync(caller, project, ct);
        if (p is null) return ToolResult.Error($"No project with key {project}.");

        Result<List<TaskDto>> tasks;
        switch ((scope ?? "all").Trim().ToLowerInvariant())
        {
            case "all":
                tasks = await _tasks.GetByProjectAsync(p.Id, caller.CompanyId, caller.UserId, false, ct);
                break;
            case "backlog":
                tasks = await _tasks.GetBacklogAsync(p.Id, caller.CompanyId, caller.UserId, false, ct);
                break;
            case "active-sprint":
                var sprints = await _sprints.GetByProjectAsync(caller.CompanyId, p.Id, caller.UserId, false, false, ct);
                if (!sprints.IsSuccess) return ToolResult.Error(sprints.Errors, p.Key);
                var active = sprints.Value!.FirstOrDefault(s => s.IsActive);
                if (active is null) return ToolResult.Ok($"{p.Key} has no active sprint.");
                tasks = await _tasks.GetBySprintAsync(active.Id, caller.CompanyId, caller.UserId, false, ct);
                break;
            default:
                return ToolResult.Error("scope must be all, backlog or active-sprint.");
        }

        if (!tasks.IsSuccess) return ToolResult.Error(tasks.Errors, p.Key);

        var matching = tasks.Value!
            .Where(t => status is null || string.Equals(t.Status, status.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matching.Count == 0) return ToolResult.Ok("No matching tasks.");

        var names = await _lookups.MemberNamesAsync(caller, p.Id, ct);
        return ToolResult.Ok(string.Join("\n", matching.Select(t => McpLookups.TaskLine(t, names))));
    }

    [McpServerTool(Name = "get_task"), Description("Shows one task in full, including its description.")]
    public async Task<CallToolResult> GetTask(
        ClaimsPrincipal? user,
        [Description("Task key, for example TS-12")] string key,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var result = await _tasks.GetByKeyAsync(key, caller.CompanyId, caller.UserId, false, ct);
        if (!result.IsSuccess) return ToolResult.Error(result.Errors);

        var t = result.Value!;
        var names = t.ProjectId is int projectId
            ? await _lookups.MemberNamesAsync(caller, projectId, ct)
            : new Dictionary<string, string>();

        return ToolResult.Ok(string.Join("\n", new[]
        {
            McpLookups.TaskLine(t, names),
            $"Story points: {(t.StoryPoints?.ToString() ?? "none")}",
            $"Sprint: {(t.SprintId is null ? "backlog" : $"#{t.SprintId}")}",
            "",
            string.IsNullOrWhiteSpace(t.Description) ? "(no description)" : t.Description,
        }));
    }
}
