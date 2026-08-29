using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.GitHub;
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
    private readonly IGitHubTaskActivityService _gitHubActivityService;
    private readonly IGitHubBranchService _gitHubBranchService;
    private readonly ITaskActivityRefreshService _taskActivityRefresh;

    public TasksController(
        ITaskService taskService,
        IGitHubTaskActivityService gitHubActivityService,
        IGitHubBranchService gitHubBranchService,
        ITaskActivityRefreshService taskActivityRefresh)
    {
        _taskService = taskService;
        _gitHubActivityService = gitHubActivityService;
        _gitHubBranchService = gitHubBranchService;
        _taskActivityRefresh = taskActivityRefresh;
    }

    [HttpGet("{taskId:int}")]
    public async Task<IActionResult> GetById(int taskId, CancellationToken ct)
    {
        var result = await _taskService.GetByIdAsync(taskId, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [HttpGet("by-key/{key}")]
    public async Task<IActionResult> GetByKey(string key, CancellationToken ct)
    {
        var result = await _taskService.GetByKeyAsync(key, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetByProject(int projectId, CancellationToken ct)
    {
        var result = await _taskService.GetByProjectAsync(projectId, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [HttpGet("project/{projectId:int}/backlog")]
    public async Task<IActionResult> GetBacklog(int projectId, CancellationToken ct)
    {
        var result = await _taskService.GetBacklogAsync(projectId, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [HttpGet("sprint/{sprintId:int}")]
    public async Task<IActionResult> GetBySprint(int sprintId, CancellationToken ct)
    {
        var result = await _taskService.GetBySprintAsync(sprintId, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    /// <summary>
    /// A task's GitHub activity. Inherits the controller's CompanyOrUser gate — membership is
    /// enforced in the service, so a non-member gets the "Auth.Forbidden" code rather than a
    /// bare framework 403, and cannot tell a missing task from a forbidden one.
    /// Not audited: reads are not audited on this controller.
    /// </summary>
    [HttpGet("{taskId:int}/github-activity")]
    public async Task<IActionResult> GetGitHubActivity(int taskId, CancellationToken ct)
    {
        var result = await _gitHubActivityService.GetForTaskAsync(CompanyId, UserId, IsCompanyAdmin, taskId, ct);
        return FromResult(result);
    }

    /// <summary>
    /// The branch name this task would get, and the repositories its project links. Inherits
    /// the controller's CompanyOrUser gate — membership is enforced in the service, the same
    /// way the activity read does it. Not audited: reads are not audited on this controller.
    /// </summary>
    [HttpGet("{taskId:int}/github-branch/suggestion")]
    public async Task<IActionResult> SuggestGitHubBranch(int taskId, CancellationToken ct)
    {
        var result = await _gitHubBranchService.GetSuggestionAsync(CompanyId, UserId, IsCompanyAdmin, taskId, ct);
        return FromResult(result);
    }

    /// <summary>
    /// Creates the branch on GitHub. A write to someone else's system, so it is audited — and
    /// unlike the company-wide activity sync, a project Member may call it: the repo↔project
    /// link is what authorizes, and it is checked per repository in the service.
    /// </summary>
    [Audit("Created a GitHub branch")]
    [HttpPost("{taskId:int}/github-branch")]
    public async Task<IActionResult> CreateGitHubBranch(int taskId, [FromBody] CreateBranchDto dto, CancellationToken ct)
    {
        var result = await _gitHubBranchService.CreateForTaskAsync(CompanyId, UserId, IsCompanyAdmin, taskId, dto, ct);
        return FromResult(result);
    }

    /// <summary>
    /// Refreshes the branches, commits and pull requests of this task's project's repositories,
    /// then applies any merge → Done transitions. Fired by opening the Activity tab, so it is
    /// reachable by members: task access is what authorizes it, checked in the service. Not
    /// audited — see TaskActivityRefreshEndpointTests.
    /// </summary>
    [HttpPost("{taskId:int}/github-refresh")]
    public async Task<IActionResult> RefreshGitHubActivity(int taskId, CancellationToken ct)
    {
        var result = await _taskActivityRefresh.RefreshAsync(
            CompanyId, taskId, UserId, IsCompanyAdmin, User.FindFirst(ClaimTypes.Name)?.Value, ct);

        return FromResult(result);
    }

    [Audit("Created a task")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskDto dto, CancellationToken ct)
    {
        var result = await _taskService.CreateAsync(dto, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [Audit("Updated a task")]
    [HttpPut("{taskId:int}")]
    public async Task<IActionResult> Update(int taskId, [FromBody] UpdateTaskDto dto, CancellationToken ct)
    {
        var result = await _taskService.UpdateAsync(taskId, dto, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [Audit("Deleted a task")]
    [HttpDelete("{taskId:int}")]
    public async Task<IActionResult> Delete(int taskId, CancellationToken ct)
    {
        var result = await _taskService.DeleteAsync(taskId, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [Audit("Moved task to sprint")]
    [HttpPatch("{taskId:int}/move-to-sprint/{sprintId:int}")]
    public async Task<IActionResult> MoveToSprint(int taskId, int sprintId, CancellationToken ct)
    {
        var result = await _taskService.MoveToSprintAsync(taskId, sprintId, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [Audit("Moved task to backlog")]
    [HttpPatch("{taskId:int}/move-to-backlog")]
    public async Task<IActionResult> MoveToBacklog(int taskId, CancellationToken ct)
    {
        var result = await _taskService.MoveToBacklogAsync(taskId, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [Audit("Changed task status")]
    [HttpPatch("{taskId:int}/status")]
    public async Task<IActionResult> SetStatus(int taskId, [FromQuery] string status, CancellationToken ct)
    {
        var result = await _taskService.SetStatusAsync(taskId, status, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }

    [Audit("Assigned task")]
    [HttpPatch("{taskId:int}/assignee")]
    public async Task<IActionResult> Assign(int taskId, [FromQuery] string? assigneeUserId, CancellationToken ct)
    {
        var result = await _taskService.AssignAsync(taskId, assigneeUserId, CompanyId, UserId, IsCompanyAdmin, ct);
        return FromResult(result);
    }
}
