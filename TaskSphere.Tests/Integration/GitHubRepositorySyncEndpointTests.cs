using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Filters;
using TaskSphere.Controllers;
using TaskSphere.Domain.Enums;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The repository refresh is company-wide GitHub spend, so it stays on GitHubController's
/// Company-only gate — unlike the project and task refreshes, which are member-reachable
/// because a repository↔project link authorizes them.
/// </summary>
public class GitHubRepositorySyncEndpointTests
{
    [Fact]
    public void The_sync_action_inherits_the_controllers_company_only_gate()
    {
        var controller = typeof(GitHubController);
        var action = controller.GetMethod(nameof(GitHubController.RefreshRepositories));

        Assert.NotNull(action);

        // Positive anchor: the gate must come from the controller, and the action must not
        // widen it. Asserting only the absence of an attribute would keep passing if the
        // controller-level gate were later removed.
        Assert.Equal(Roles.Company, controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Null(action!.GetCustomAttribute<AuthorizeAttribute>());

        // The client hardcodes this route string, and nothing else connects the two.
        Assert.Equal("repositories/sync", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void The_sync_action_is_deliberately_not_audited()
    {
        var action = typeof(GitHubController).GetMethod(nameof(GitHubController.RefreshRepositories));

        // It fires from returning to the tab, not from a decision. Auditing it would file one
        // row per refocus. The link/unlink actions it enables are audited individually.
        Assert.Null(action!.GetCustomAttribute<AuditAttribute>());
    }

    [Fact]
    public void The_sync_action_takes_no_parameters_but_the_cancellation_token()
    {
        var action = typeof(GitHubController).GetMethod(nameof(GitHubController.RefreshRepositories));

        // The whole point of this endpoint is that it trusts nothing from the caller: the
        // installation is resolved from the authenticated company. A parameter here would be a
        // way to name someone else's installation.
        var parameters = action!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(CancellationToken), parameters[0].ParameterType);
    }
}
