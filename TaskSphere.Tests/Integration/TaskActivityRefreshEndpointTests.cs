using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Controllers;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Task;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Tests.Integration;

public class TaskActivityRefreshEndpointTests
{
    [Fact]
    public void TheRefreshEndpoint_IsNotAudited()
    {
        var method = typeof(TasksController).GetMethod(nameof(TasksController.RefreshGitHubActivity));

        // Fired by opening a panel. Auditing it would bury every real action in the log.
        Assert.Null(method!.GetCustomAttribute<AuditAttribute>());
    }

    [Fact]
    public void TheRefreshEndpoint_IsOnTheMemberReachableController()
    {
        var authorize = typeof(TasksController).GetCustomAttribute<AuthorizeAttribute>();

        // The whole point of the slice: a project member can refresh their own task's activity,
        // which the Company-only Sync button has never allowed.
        Assert.Equal(Roles.CompanyOrUser, authorize!.Roles);
    }

    [Fact]
    public void TheRefreshEndpoint_IsAPostOnTheTaskScopedRoute()
    {
        var method = typeof(TasksController).GetMethod(nameof(TasksController.RefreshGitHubActivity));
        var post = method!.GetCustomAttribute<HttpPostAttribute>();

        Assert.NotNull(post);
        Assert.Equal("{taskId:int}/github-refresh", post!.Template);
    }
}
