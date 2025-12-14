using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Domain.Common;

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

        return BadRequest(result.Errors);
    }
    
    protected bool TryGetCompanyId(out Guid companyId)
    {
        companyId = default;
        var value = User.FindFirst("companyId")?.Value;
        return Guid.TryParse(value, out companyId);
    }
}