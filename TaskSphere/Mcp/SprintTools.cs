using System.ComponentModel;
using System.Security.Claims;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaskSphere.Application.Interfaces;

namespace TaskSphere.Mcp;

/// <summary>
/// Moving tasks between sprints only. Creating, activating and archiving sprints stays with
/// humans — Claude can propose it, a person does it.
/// </summary>
[McpServerToolType]
public sealed class SprintTools
{
    private readonly ISprintService _sprints;
    private readonly ITaskService _tasks;
    private readonly McpLookups _lookups;
    private readonly McpAudit _audit;

    public SprintTools(ISprintService sprints, ITaskService tasks, McpLookups lookups, McpAudit audit)
    {
        _sprints = sprints;
        _tasks = tasks;
        _lookups = lookups;
        _audit = audit;
    }

    [McpServerTool(Name = "list_sprints"), Description("Lists a project's non-archived sprints as #id Name, marking the active one.")]
    public async Task<CallToolResult> ListSprints(
        ClaimsPrincipal? user,
        [Description("Project key, for example TS")] string project,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var p = await _lookups.ProjectAsync(caller, project, ct);
        if (p is null) return ToolResult.Error($"No project with key {project}.");

        var sprints = await _sprints.GetByProjectAsync(caller.CompanyId, p.Id, caller.UserId, false, false, ct);
        if (!sprints.IsSuccess) return ToolResult.Error(sprints.Errors, p.Key);
        if (sprints.Value!.Count == 0) return ToolResult.Ok($"{p.Key} has no sprints.");

        return ToolResult.Ok(string.Join("\n", sprints.Value.Select(s =>
            $"#{s.Id} {s.Name}{(s.IsActive ? " (active)" : "")} {s.StartDate:yyyy-MM-dd} to {s.EndDate:yyyy-MM-dd}")));
    }

    [McpServerTool(Name = "move_to_sprint"), Description("Moves a task into a sprint. Get sprint ids from list_sprints.")]
    public async Task<CallToolResult> MoveToSprint(
        ClaimsPrincipal? user,
        [Description("Task key, for example TS-12")] string key,
        [Description("Sprint id from list_sprints")] int sprintId,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var args = new { key, sprintId };

        var task = await _tasks.GetByKeyAsync(key, caller.CompanyId, caller.UserId, false, ct);
        if (!task.IsSuccess)
        {
            _audit.Record(caller, "move_to_sprint", "Moved task to sprint", McpAudit.StatusFor(false, task.Errors), args);
            return ToolResult.Error(task.Errors);
        }

        var result = await _tasks.MoveToSprintAsync(task.Value!.Id, sprintId, caller.CompanyId, caller.UserId, false, ct);
        _audit.Record(caller, "move_to_sprint", "Moved task to sprint", McpAudit.StatusFor(result), args);
        return result.IsSuccess ? ToolResult.Ok($"Moved {task.Value.Key} to sprint #{sprintId}.") : ToolResult.Error(result.Errors);
    }

    [McpServerTool(Name = "move_to_backlog"), Description("Moves a task out of its sprint and back to the backlog.")]
    public async Task<CallToolResult> MoveToBacklog(
        ClaimsPrincipal? user,
        [Description("Task key, for example TS-12")] string key,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var args = new { key };

        var task = await _tasks.GetByKeyAsync(key, caller.CompanyId, caller.UserId, false, ct);
        if (!task.IsSuccess)
        {
            _audit.Record(caller, "move_to_backlog", "Moved task to backlog", McpAudit.StatusFor(false, task.Errors), args);
            return ToolResult.Error(task.Errors);
        }
        if (task.Value!.SprintId is null)
            return ToolResult.Ok($"{task.Value.Key} is already in the backlog.");

        var result = await _tasks.MoveToBacklogAsync(task.Value.Id, caller.CompanyId, caller.UserId, false, ct);
        _audit.Record(caller, "move_to_backlog", "Moved task to backlog", McpAudit.StatusFor(result), args);
        return result.IsSuccess ? ToolResult.Ok($"Moved {task.Value.Key} to the backlog.") : ToolResult.Error(result.Errors);
    }
}
