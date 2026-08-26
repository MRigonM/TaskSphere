using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Controllers;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Project;
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

    private sealed class FakeRefreshService : IProjectActivityRefreshService
    {
        public bool? ReceivedIsCompanyAdmin { get; private set; }
        public Guid ReceivedCompanyId { get; private set; }
        public int ReceivedProjectId { get; private set; }

        public Task<Result<ProjectActivityRefreshDto>> RefreshAsync(
            Guid companyId,
            int projectId,
            string userId,
            bool isCompanyAdmin,
            string? actorUsername,
            CancellationToken cancellationToken = default)
        {
            ReceivedCompanyId = companyId;
            ReceivedProjectId = projectId;
            ReceivedIsCompanyAdmin = isCompanyAdmin;
            return Task.FromResult(
                Result<ProjectActivityRefreshDto>.Success(
                    new ProjectActivityRefreshDto(false, 0, 0)));
        }
    }

    private sealed class FakeProjectService : IProjectService
    {
        public Task<Result<ProjectDto>> CreateAsync(Guid companyId, CreateProjectDto dto, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Result<IEnumerable<ProjectDto>>> GetAllAsync(Guid companyId, string userId, bool isCompanyAdmin, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Result<ProjectDto>> GetByIdAsync(Guid companyId, int projectId, string userId, bool isCompanyAdmin, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Result<ProjectDto>> UpdateSettingsAsync(Guid companyId, int projectId, UpdateProjectSettingsDto dto, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Result<IEnumerable<MemberDto>>> GetMembersAsync(Guid companyId, int projectId, string userId, bool isCompanyAdmin, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Result<string>> AddMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Result<string>> RemoveMemberAsync(Guid companyId, int projectId, string userId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Result<IEnumerable<ProjectDto>>> GetMembersProjects(Guid companyId, string userId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async System.Threading.Tasks.Task Company_admin_users_pass_true_to_the_service()
    {
        var companyId = Guid.NewGuid();
        var projectId = 42;
        var userId = "user-1";

        var service = new FakeRefreshService();
        var controller = new ProjectsController(new FakeProjectService(), service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, userId),
                        new Claim(ClaimTypes.Role, Roles.Company)
                    },
                    authenticationType: "test"))
            }
        };
        controller.ControllerContext.HttpContext.Items["CompanyId"] = companyId;

        await controller.RefreshGitHub(projectId, default);

        Assert.NotNull(service.ReceivedIsCompanyAdmin);
        Assert.True(service.ReceivedIsCompanyAdmin.Value);
        Assert.Equal(projectId, service.ReceivedProjectId);
    }

    [Fact]
    public async System.Threading.Tasks.Task Non_admin_users_pass_false_to_the_service()
    {
        var companyId = Guid.NewGuid();
        var projectId = 42;
        var userId = "user-2";

        var service = new FakeRefreshService();
        var controller = new ProjectsController(new FakeProjectService(), service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, userId),
                        new Claim(ClaimTypes.Role, Roles.User)
                    },
                    authenticationType: "test"))
            }
        };
        controller.ControllerContext.HttpContext.Items["CompanyId"] = companyId;

        await controller.RefreshGitHub(projectId, default);

        Assert.NotNull(service.ReceivedIsCompanyAdmin);
        Assert.False(service.ReceivedIsCompanyAdmin.Value);
        Assert.Equal(projectId, service.ReceivedProjectId);
    }
}
