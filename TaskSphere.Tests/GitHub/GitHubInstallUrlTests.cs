using System.Reflection;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskSphere.Controllers;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;
using TaskSphere.Infrastructure.Services;

namespace TaskSphere.Tests.GitHub;

public class GitHubInstallUrlTests
{
    private static readonly Guid Company = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static (GitHubConnectionService Service, GitHubInstallStateService State) NewService()
    {
        var provider = DataProtectionProvider.Create(nameof(GitHubInstallUrlTests));
        var state = new GitHubInstallStateService(provider);

        var options = GitHubAppOptionsAndJwtTests.ValidOptions();
        options.AppSlug = "tasksphere-dev";

        return (NewConnectionService(options, state), state);
    }

    // GetInstallUrl reaches only the options and the state service; the rest of the constructor
    // belongs to ConnectAsync, which GitHubConnectTests covers against a real database.
    private static GitHubConnectionService NewConnectionService(
        Infrastructure.Configuration.GitHubAppOptions options,
        GitHubInstallStateService state)
        => new GitHubConnectionService(
            Options.Create(options),
            state,
            userAuthService: null!,
            unitOfWork: null!,
            syncService: null!,
            mapper: null!);

    [Fact]
    public void GetInstallUrl_PointsAtTheAppsInstallationsEndpoint()
    {
        var (service, _) = NewService();

        var result = service.GetInstallUrl(Company, "user-abc");

        Assert.True(result.IsSuccess);

        var uri = new Uri(result.Value!.Url);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("github.com", uri.Host);
        Assert.Equal("/apps/tasksphere-dev/installations/new", uri.AbsolutePath);
    }

    [Fact]
    public void GetInstallUrl_CarriesAStateThatUnprotectsToTheCaller()
    {
        var (service, state) = NewService();

        var result = service.GetInstallUrl(Company, "user-abc");
        var uri = new Uri(result.Value!.Url);

        var stateParam = HttpUtility.ParseQueryString(uri.Query)["state"];
        Assert.False(string.IsNullOrWhiteSpace(stateParam));

        var unprotected = state.Unprotect(stateParam!);

        Assert.True(unprotected.IsSuccess);
        Assert.Equal(Company, unprotected.Value!.CompanyId);
        Assert.Equal("user-abc", unprotected.Value.UserId);
    }

    [Fact]
    public void GetInstallUrl_EncodesTheState()
    {
        // Protected payloads are base64url, but the value goes into a query string either way —
        // building the URL by concatenation without encoding is how a '+' silently becomes a
        // space and the callback then rejects a state the user never tampered with.
        var (service, _) = NewService();

        var url = service.GetInstallUrl(Company, "user-abc").Value!.Url;
        var raw = url[(url.IndexOf("state=", StringComparison.Ordinal) + "state=".Length)..];

        Assert.DoesNotContain(' ', raw);
        Assert.Equal(Uri.EscapeDataString(Uri.UnescapeDataString(raw)), raw);
    }

    [Fact]
    public void GetInstallUrl_FailsWhenTheAppSlugIsNotConfigured()
    {
        var provider = DataProtectionProvider.Create(nameof(GitHubInstallUrlTests));
        var options = GitHubAppOptionsAndJwtTests.ValidOptions();
        options.AppSlug = "";

        var service = NewConnectionService(options, new GitHubInstallStateService(provider));

        var result = service.GetInstallUrl(Company, "user-abc");

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.NotConfigured", result.Errors[0].Code);
    }

    // The role gate is enforced by the framework, not by code this project can call directly.
    // These assert the attributes that produce it: the bare 403 of §0h comes from
    // [Authorize(Roles = Company)], and CompanyId throws without [RequireCompany]. A pipeline
    // test would need WebApplicationFactory, which cannot boot here — GitHubAppOptions is
    // registered ValidateOnStart and there are no App credentials in test configuration.
    [Fact]
    public void GitHubController_IsGatedToTheCompanyRole()
    {
        var authorize = typeof(GitHubController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Roles.Company, authorize!.Roles);
    }

    [Fact]
    public void GitHubController_RequiresCompany_SoCompanyIdResolves()
    {
        Assert.NotNull(typeof(GitHubController).GetCustomAttribute<RequireCompanyAttribute>());
    }

    [Fact]
    public void GetCompanyLinks_DoesNotWidenTheRoleGate()
    {
        // GetProjectRepositories deliberately widens to CompanyOrUser because a project Member
        // can reach it. The company-wide read must NOT: it returns every project's links, so it
        // stays on the controller's Company-only gate. An [Authorize] here would be a leak.
        var action = typeof(GitHubController).GetMethod(nameof(GitHubController.GetCompanyLinks));

        Assert.NotNull(action);
        Assert.Null(action!.GetCustomAttribute<AuthorizeAttribute>());

        // The client hardcodes this route string, and nothing else connects the two.
        Assert.Equal("links", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }
}
