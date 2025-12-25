using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaskSphere.Filters;

public sealed class RequireCompanyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var value = context.HttpContext.User.FindFirst("companyId")?.Value;

        if (!Guid.TryParse(value, out var companyId))
        {
            context.Result = new UnauthorizedObjectResult("Missing companyId claim.");
            return;
        }

        context.HttpContext.Items["CompanyId"] = companyId;
        await next();
    }
}