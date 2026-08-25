using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Controllers;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The first member-reachable GitHub spend in the app. It lives on ProjectsController rather
/// than GitHubController for the same reason the activity read lives on TasksController: the
/// Company-only gate belongs to company-wide operations, and this one is project-scoped.
/// </summary>
public class ProjectActivityRefreshEndpointTests
{
    [Fact]
    public void The_refresh_action_inherits_the_controllers_company_or_user_gate()
    {
        var controller = typeof(ProjectsController);
        var action = controller.GetMethod(nameof(ProjectsController.RefreshGitHub));

        Assert.NotNull(action);

        // Not on the action: membership is enforced in the service so the response carries
        // "Auth.Forbidden" rather than being a bare framework 403.
        Assert.Null(action!.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(Roles.CompanyOrUser, controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles);

        // The client hardcodes this route string, and nothing else connects the two.
        Assert.Equal("{projectId:int}/github-refresh", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void The_refresh_action_is_deliberately_not_audited()
    {
        var action = typeof(ProjectsController).GetMethod(nameof(ProjectsController.RefreshGitHub));

        // Every audited action is a human decision. This one fires from opening a page, and
        // auditing it would bury the merge → Done entries under one row per board visit. The
        // transitions it causes are still audited individually.
        Assert.Null(action!.GetCustomAttribute<AuditAttribute>());
    }
}
