using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaskSphere.Controllers;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Tests.Integration;

public class AgentsEndpointTests
{
    [Fact]
    public void The_controller_is_for_humans_of_either_role_and_company_scoped()
    {
        var authorize = typeof(AgentsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Roles.CompanyOrUser, authorize!.Roles);
        // No AuthenticationSchemes: the default (JWT) applies, so an agent token can never
        // regenerate or revoke itself.
        Assert.Null(authorize.AuthenticationSchemes);
        Assert.NotNull(typeof(AgentsController).GetCustomAttribute<RequireCompanyAttribute>());
    }

    [Theory]
    [InlineData(nameof(AgentsController.Create), typeof(HttpPostAttribute), "me", true)]
    [InlineData(nameof(AgentsController.GetMine), typeof(HttpGetAttribute), "me", false)]
    [InlineData(nameof(AgentsController.RegenerateToken), typeof(HttpPostAttribute), "me/token", true)]
    [InlineData(nameof(AgentsController.RevokeToken), typeof(HttpDeleteAttribute), "me/token", true)]
    public void Each_action_is_on_its_route_and_writes_are_audited(string action, Type verb, string template, bool audited)
    {
        var method = typeof(AgentsController).GetMethod(action)!;
        var http = (HttpMethodAttribute)method.GetCustomAttribute(verb)!;

        Assert.Equal(template, http.Template);
        Assert.Equal(audited, method.GetCustomAttribute<AuditAttribute>() is not null);
    }
}
