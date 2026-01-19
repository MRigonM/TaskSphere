using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Project;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize(Roles = Roles.CompanyOrUser)]
[RequireCompany]
[Route("api/[controller]")]
public class ProjectsController : ApiBaseController
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [Authorize(Roles = Roles.Company)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectDto dto, CancellationToken ct)
    {
        var result = await _projectService.CreateAsync(CompanyId, dto, ct);
        return FromResult(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _projectService.GetAllAsync(CompanyId, ct);
        return FromResult(result);
    }
    
    [Authorize(Roles = Roles.CompanyOrUser)]
    [HttpGet("{projectId:int}")]
    public async Task<IActionResult> GetById(int projectId, CancellationToken ct)
    {
        var result = await _projectService.GetByIdAsync(CompanyId, projectId, ct);
        return FromResult(result);
    }
    
    [Authorize(Roles = Roles.User)]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMembersProjects(CancellationToken ct)
    {
        var result = await _projectService.GetMembersProjects(CompanyId, UserId, ct);
        return FromResult(result);
    }

    [Authorize(Roles = Roles.CompanyOrUser)]
    [HttpGet("{projectId:int}/members")]
    public async Task<IActionResult> Members(int projectId, CancellationToken ct)
    {
        var result = await _projectService.GetMembersAsync(CompanyId, projectId, ct);
        return FromResult(result);
    }

    [Authorize(Roles = Roles.Company)]
    [HttpPost("{projectId:int}/members")]
    public async Task<IActionResult> AddMember(int projectId, [FromBody] AddMemberDto dto, CancellationToken ct)
    {
        var result = await _projectService.AddMemberAsync(CompanyId, projectId, dto.UserId, ct);
        return FromResult(result);
    }

    [Authorize(Roles = Roles.Company)]
    [HttpDelete("{projectId:int}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(int projectId, string userId, CancellationToken ct)
    {
        var result = await _projectService.RemoveMemberAsync(CompanyId, projectId, userId, ct);
        return FromResult(result);
    }
}