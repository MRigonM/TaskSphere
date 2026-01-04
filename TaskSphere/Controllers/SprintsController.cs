using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize(Roles = Roles.Company)]
[RequireCompany]
[Route("api/[controller]")]
public class SprintsController : ApiBaseController
{
    private readonly ISprintService _sprintService;

    public SprintsController(ISprintService sprintService)
    {
        _sprintService = sprintService;
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetByProject(int projectId, CancellationToken ct)
    {
        var result = await _sprintService.GetByProjectAsync(CompanyId, projectId, ct);
        return FromResult(result);
    }

    [HttpGet("{sprintId:int}")]
    public async Task<IActionResult> GetById(int sprintId, CancellationToken ct)
    {
        var result = await _sprintService.GetByIdAsync(CompanyId, sprintId, ct);
        return FromResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSprintDto dto, CancellationToken ct)
    {
        var result = await _sprintService.CreateAsync(CompanyId, dto, ct);
        return FromResult(result);
    }

    [HttpPut("{sprintId:int}")]
    public async Task<IActionResult> Update(int sprintId, [FromBody] UpdateSprintDto dto, CancellationToken ct)
    {
        var result = await _sprintService.UpdateAsync(CompanyId, sprintId, dto, ct);
        return FromResult(result);
    }

    [HttpPatch("{sprintId:int}/active")]
    public async Task<IActionResult> SetActive(int sprintId, [FromQuery] bool isActive = true, CancellationToken ct = default)
    {
        var result = await _sprintService.SetActiveAsync(CompanyId, sprintId, isActive, ct);
        return FromResult(result);
    }

    [HttpPost("{sprintId:int}/activate")]
    public async Task<IActionResult> ActivateExistingAndCarryOver(int sprintId, [FromQuery] bool carryOverUnfinished = true, CancellationToken ct = default)
    {
        var result = await _sprintService.ActivateExistingAndCarryOverAsync(CompanyId, sprintId, carryOverUnfinished, ct);
        return FromResult(result);
    }

    [HttpGet("{sprintId:int}/board")]
    public async Task<IActionResult> Board(int sprintId, CancellationToken ct)
    {
        var result = await _sprintService.GetBoardAsync(CompanyId, sprintId, ct);
        return FromResult(result);
    }

    [HttpPost("{sprintId:int}/tasks/{taskId:int}/move-to-active")]
    public async Task<IActionResult> MoveTaskToActive(int sprintId, int taskId, CancellationToken ct)
    {
        var result = await _sprintService.MoveTaskToActiveAsync(CompanyId, sprintId, taskId, ct);
        return FromResult(result);
    }
}
