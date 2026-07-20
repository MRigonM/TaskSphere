using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize(Roles = Roles.Company)]
[RequireCompany]
[Route("api/[controller]")]
public class AuditController : ApiBaseController
{
    private readonly IAuditService _auditService;

    public AuditController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] AuditQueryDto query, CancellationToken ct)
    {
        var result = await _auditService.GetPagedAsync(CompanyId, query, ct);
        return Ok(result);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct, [FromQuery] int days = 30)
    {
        var result = await _auditService.GetStatsAsync(CompanyId, days, ct);
        return Ok(result);
    }
}
