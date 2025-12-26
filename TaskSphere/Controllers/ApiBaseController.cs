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
    
    protected Guid CompanyId
        => HttpContext.Items.TryGetValue("CompanyId", out var v) && v is Guid id
            ? id
            : throw new InvalidOperationException("CompanyId not set. Add [RequireCompany] on this controller/action.");
}