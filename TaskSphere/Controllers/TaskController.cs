using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Task;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize(Roles = Roles.CompanyOrUser)]
[RequireCompany]
[Route("api/[controller]")]
public class TasksController : ApiBaseController
{
    private readonly ITaskService _taskService;

    public TasksController(ITaskService taskService)
    {
        _taskService = taskService;
    }

    [HttpGet("{taskId:int}")]
    public async Task<IActionResult> GetById(int taskId, CancellationToken ct)
    {
        var result = await _taskService.GetByIdAsync(taskId, CompanyId, ct);
        return FromResult(result);
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetByProject(int projectId, CancellationToken ct)
    {
        var result = await _taskService.GetByProjectAsync(projectId, CompanyId, ct);
        return FromResult(result);
    }

    [HttpGet("project/{projectId:int}/backlog")]
    public async Task<IActionResult> GetBacklog(int projectId, CancellationToken ct)
    {
        var result = await _taskService.GetBacklogAsync(projectId, CompanyId, ct);
        return FromResult(result);
    }

    [HttpGet("sprint/{sprintId:int}")]
    public async Task<IActionResult> GetBySprint(int sprintId, CancellationToken ct)
    {
        var result = await _taskService.GetBySprintAsync(sprintId, CompanyId, ct);
        return FromResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskDto dto, CancellationToken ct)
    {
        var result = await _taskService.CreateAsync(dto, CompanyId, UserId, ct);
        return FromResult(result);
    }

    [HttpPut("{taskId:int}")]
    public async Task<IActionResult> Update(int taskId, [FromBody] UpdateTaskDto dto, CancellationToken ct)
    {
        var result = await _taskService.UpdateAsync(taskId, dto, CompanyId, ct);
        return FromResult(result);
    }

    [HttpDelete("{taskId:int}")]
    public async Task<IActionResult> Delete(int taskId, CancellationToken ct)
    {
        var result = await _taskService.DeleteAsync(taskId, CompanyId, ct);
        return FromResult(result);
    }

    [HttpPatch("{taskId:int}/move-to-sprint/{sprintId:int}")]
    public async Task<IActionResult> MoveToSprint(int taskId, int sprintId, CancellationToken ct)
    {
        var result = await _taskService.MoveToSprintAsync(taskId, sprintId, CompanyId, ct);
        return FromResult(result);
    }

    [HttpPatch("{taskId:int}/move-to-backlog")]
    public async Task<IActionResult> MoveToBacklog(int taskId, CancellationToken ct)
    {
        var result = await _taskService.MoveToBacklogAsync(taskId, CompanyId, ct);
        return FromResult(result);
    }

    [HttpPatch("{taskId:int}/status")]
    public async Task<IActionResult> SetStatus(int taskId, [FromQuery] string status, CancellationToken ct)
    {
        var result = await _taskService.SetStatusAsync(taskId, status, CompanyId, ct);
        return FromResult(result);
    }

    [HttpPatch("{taskId:int}/assignee")]
    public async Task<IActionResult> Assign(int taskId, [FromQuery] string? assigneeUserId, CancellationToken ct)
    {
        var result = await _taskService.AssignAsync(taskId, assigneeUserId, CompanyId, ct);
        return FromResult(result);
    }
}