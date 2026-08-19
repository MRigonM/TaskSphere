using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Controllers;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The role split lands on the existing controllers with no new attributes. These tests pin
/// that — an [Authorize] appearing on either action later is a silent widening or narrowing
/// that nothing else would catch. Resolution of the three new services is asserted by the §B
/// gate in <see cref="GitHubDependencyInjectionTests"/> rather than duplicated here.
/// </summary>
public class GitHubActivityEndpointTests
{
    [Fact]
    public void TheActivityRead_InheritsTasksControllersCompanyOrUserGate()
    {
        var controller = typeof(TasksController);
        var action = controller.GetMethod(nameof(TasksController.GetGitHubActivity));

        Assert.NotNull(action);

        // Not on the action: membership is enforced in the service so the response carries
        // "Auth.Forbidden" rather than being a bare framework 403.
        Assert.Null(action!.GetCustomAttribute<AuthorizeAttribute>());

        var controllerGate = controller.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(Roles.CompanyOrUser, controllerGate!.Roles);

        // The client hardcodes this route string, and nothing else connects the two.
        Assert.Equal("{taskId:int}/github-activity", action.GetCustomAttribute<HttpGetAttribute>()!.Template);

        // Reads are not audited on this controller.
        Assert.Null(action.GetCustomAttribute<AuditAttribute>());
    }

    [Fact]
    public void TheSyncAction_StaysOnGitHubControllersCompanyOnlyGate_AndIsAudited()
    {
        var controller = typeof(GitHubController);
        var action = controller.GetMethod(nameof(GitHubController.SyncActivity));

        Assert.NotNull(action);
        Assert.Null(action!.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(Roles.Company, controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("activity/sync", action.GetCustomAttribute<HttpPostAttribute>()!.Template);

        // A deliberate admin action that spends installation rate limit.
        Assert.NotNull(action.GetCustomAttribute<AuditAttribute>());
    }
}
