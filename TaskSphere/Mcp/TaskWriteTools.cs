using System.ComponentModel;
using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Task;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Mcp;

[McpServerToolType]
public sealed class TaskWriteTools
{
    private readonly ITaskService _tasks;
    private readonly McpLookups _lookups;
    private readonly McpAudit _audit;
    private readonly IValidator<CreateTaskDto> _createValidator;
    private readonly IValidator<UpdateTaskDto> _updateValidator;
    private readonly IReadOnlyUnitOfWork _unitOfWork;

    public TaskWriteTools(
        ITaskService tasks,
        McpLookups lookups,
        McpAudit audit,
        IValidator<CreateTaskDto> createValidator,
        IValidator<UpdateTaskDto> updateValidator,
        IReadOnlyUnitOfWork unitOfWork)
    {
        _tasks = tasks;
        _lookups = lookups;
        _audit = audit;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _unitOfWork = unitOfWork;
    }

    [McpServerTool(Name = "create_task"), Description("Creates a task in a project's backlog with status Open. Returns its key.")]
    public async Task<CallToolResult> CreateTask(
        ClaimsPrincipal? user,
        [Description("Project key, for example TS")] string project,
        [Description("Short title")] string title,
        [Description("Optional longer description")] string? description = null,
        [Description("Low, Medium, High or Critical")] string? priority = null,
        [Description("0 to 100")] int? storyPoints = null,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var args = new { project, title, description, priority, storyPoints };

        var p = await _lookups.ProjectAsync(caller, project, ct);
        if (p is null)
        {
            _audit.Record(caller, "create_task", "Created a task", 400, args);
            return ToolResult.Error($"No project with key {project}.");
        }

        var dto = new CreateTaskDto
        {
            ProjectId = p.Id, Title = title?.Trim() ?? "", Description = description,
            Priority = priority, StoryPoints = storyPoints, Status = TaskStatuses.Open,
        };

        // MVC auto-validation never runs for MCP calls, so the controller's validator runs here.
        var validation = await _createValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            _audit.Record(caller, "create_task", "Created a task", 400, args);
            return ToolResult.Error(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var created = await _tasks.CreateAsync(dto, caller.CompanyId, caller.UserId, false, ct);
        _audit.Record(caller, "create_task", "Created a task", McpAudit.StatusFor(created.IsSuccess, created.Errors), args);
        if (!created.IsSuccess) return ToolResult.Error(created.Errors, p.Key);

        var task = await _tasks.GetByIdAsync(created.Value, caller.CompanyId, caller.UserId, false, ct);
        return ToolResult.Ok($"Created {task.Value?.Key ?? $"task #{created.Value}"}: {dto.Title}");
    }

    [McpServerTool(Name = "update_task"), Description("Changes a task's title, description, priority or story points. Arguments you leave out keep their current value.")]
    public async Task<CallToolResult> UpdateTask(
        ClaimsPrincipal? user,
        [Description("Task key, for example TS-12")] string key,
        string? title = null,
        string? description = null,
        [Description("Low, Medium, High or Critical")] string? priority = null,
        int? storyPoints = null,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var args = new { key, title, description, priority, storyPoints };

        var current = await _tasks.GetByKeyAsync(key, caller.CompanyId, caller.UserId, false, ct);
        if (!current.IsSuccess)
        {
            _audit.Record(caller, "update_task", "Updated a task", McpAudit.StatusFor(false, current.Errors), args);
            return ToolResult.Error(current.Errors);
        }

        var t = current.Value!;
        var dto = new UpdateTaskDto
        {
            Title = title?.Trim() ?? t.Title,
            Description = description ?? t.Description,
            Status = t.Status,
            Priority = priority ?? t.Priority,
            StoryPoints = storyPoints ?? t.StoryPoints,
            AssigneeUserId = t.AssigneeUserId,
            SprintId = t.SprintId,
        };

        var validation = await _updateValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            _audit.Record(caller, "update_task", "Updated a task", 400, args);
            return ToolResult.Error(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var updated = await _tasks.UpdateAsync(t.Id, dto, caller.CompanyId, caller.UserId, false, ct);
        if (!updated.IsSuccess && updated.Errors.Any(e => e.Code == "NoChanges"))
            return ToolResult.Ok($"{t.Key}: nothing changed.");

        _audit.Record(caller, "update_task", "Updated a task", McpAudit.StatusFor(updated.IsSuccess, updated.Errors), args);
        return updated.IsSuccess ? ToolResult.Ok($"Updated {t.Key}.") : ToolResult.Error(updated.Errors);
    }

    [McpServerTool(Name = "set_status"), Description("Moves a task to Open, InProgress, Blocked or Done.")]
    public async Task<CallToolResult> SetStatus(
        ClaimsPrincipal? user,
        [Description("Task key, for example TS-12")] string key,
        [Description("Open, InProgress, Blocked or Done")] string status,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var args = new { key, status };

        var current = await _tasks.GetByKeyAsync(key, caller.CompanyId, caller.UserId, false, ct);
        if (!current.IsSuccess)
        {
            _audit.Record(caller, "set_status", "Changed task status", McpAudit.StatusFor(false, current.Errors), args);
            return ToolResult.Error(current.Errors);
        }

        var t = current.Value!;
        // SetStatusAsync treats an unchanged row as a NoChanges failure; to Claude, asking for the
        // status a task already has is success.
        if (string.Equals(t.Status, status?.Trim(), StringComparison.OrdinalIgnoreCase))
            return ToolResult.Ok($"{t.Key} is already {t.Status}.");

        var result = await _tasks.SetStatusAsync(t.Id, status ?? "", caller.CompanyId, caller.UserId, false, ct);
        _audit.Record(caller, "set_status", "Changed task status", McpAudit.StatusFor(result), args);
        return result.IsSuccess
            ? ToolResult.Ok($"{t.Key} is now {(await _tasks.GetByIdAsync(t.Id, caller.CompanyId, caller.UserId, false, ct)).Value?.Status}.")
            : ToolResult.Error(result.Errors);
    }

    [McpServerTool(Name = "assign_task"), Description("Assigns a task to yourself (me), to your owner (owner), or to nobody (none).")]
    public async Task<CallToolResult> AssignTask(
        ClaimsPrincipal? user,
        [Description("Task key, for example TS-12")] string key,
        [Description("me, owner or none")] string to,
        CancellationToken ct = default)
    {
        var caller = AgentCaller.From(user);
        var args = new { key, to };

        string? assignee;
        switch ((to ?? "").Trim().ToLowerInvariant())
        {
            case "me": assignee = caller.UserId; break;
            case "none": assignee = null; break;
            case "owner":
                // Read through a Members row: the agent is always a member of the project it works in.
                var ownerId = await _unitOfWork.Members.GetAll()
                    .Where(m => m.UserId == caller.UserId)
                    .Select(m => m.User.OwnerUserId)
                    .FirstOrDefaultAsync(ct);
                if (ownerId is null) return ToolResult.Error("Could not find your owner.");
                assignee = ownerId;
                break;
            default:
                return ToolResult.Error("to must be me, owner or none.");
        }

        var current = await _tasks.GetByKeyAsync(key, caller.CompanyId, caller.UserId, false, ct);
        if (!current.IsSuccess)
        {
            _audit.Record(caller, "assign_task", "Assigned task", McpAudit.StatusFor(false, current.Errors), args);
            return ToolResult.Error(current.Errors);
        }

        var t = current.Value!;
        if (t.AssigneeUserId == assignee)
            return ToolResult.Ok($"{t.Key} is already assigned that way.");

        var result = await _tasks.AssignAsync(t.Id, assignee, caller.CompanyId, caller.UserId, false, ct);
        _audit.Record(caller, "assign_task", "Assigned task", McpAudit.StatusFor(result), args);
        return result.IsSuccess ? ToolResult.Ok($"Assigned {t.Key} to {to}.") : ToolResult.Error(result.Errors);
    }
}
