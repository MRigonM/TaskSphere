using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;

namespace TaskSphere.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ApiBaseController : ControllerBase
{
    protected IActionResult FromResult<T>(Result<T> result)
    {
        if (result.IsSuccess && result.Value is not null)
            return Ok(result.Value);

        if (result.IsSuccess)
            return Ok();

        if (result.Errors.Any(e => e.Code == "Auth.Forbidden"))
            return StatusCode(StatusCodes.Status403Forbidden, result.Errors);

        return BadRequest(result.Errors);
    }
    
    protected Guid CompanyId
        => HttpContext.Items.TryGetValue("CompanyId", out var v) && v is Guid id
            ? id
            : throw new InvalidOperationException("CompanyId not set. Add [RequireCompany] on this controller/action.");
    
    protected string UserId
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("UserId claim not found.");

    protected bool IsCompanyAdmin => User.IsInRole(Roles.Company);
}
