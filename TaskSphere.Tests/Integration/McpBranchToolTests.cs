using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// create_branch through /mcp as the agent, with GitHub faked. TS links rigon-org/api; a test that
/// needs two repositories links rigon-org/web too. OT is a project the agent is not in.
/// </summary>
public class McpBranchToolTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMcpBranchToolTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const string RefBody = "{\"ref\":\"refs/heads/main\",\"object\":{\"sha\":\"basesha123\"}}";

    private readonly FakeGitHubApiClient _github = new();
    private McpTestHost _host = null!;
    private McpClient _client = null!;
    private Guid _companyId;
    private int _projectId;
    private int _otherProjectId;
    private int _apiRepositoryId;
    private int _webRepositoryId;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async SystemTask.Task InitializeAsync()
    {
        string token;
        await using (var db = NewContext())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            var company = new Company { Name = "Branch Tool Co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            _companyId = company.Id;

            var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
            var otherProject = new Project { Name = "Other Team", Key = "OT", CompanyId = _companyId };
            db.Projects.AddRange(project, otherProject);
            await db.SaveChangesAsync();
            _projectId = project.Id;
            _otherProjectId = otherProject.Id;

            var h = await AgentTestHarness.CreateAsync(db);
            var human = await h.SeedHumanAsync(_companyId, "Rigon", Roles.User);
            var agent = await h.SeedAgentAsync(human);
            db.Members.AddRange(
                new Member { ProjectId = _projectId, UserId = human.Id },
                new Member { ProjectId = _projectId, UserId = agent.Id });

            var installation = new GitHubInstallation
            {
                InstallationId = 7401,
                CompanyId = _companyId,
                AccountLogin = "rigon-org",
                AccountType = "Organization",
                RepositorySelection = RepositorySelection.All,
            };
            db.GitHubInstallations.Add(installation);
            await db.SaveChangesAsync();

            var api = new GitHubRepository
            {
                RepositoryId = 555, GitHubInstallationId = installation.Id, CompanyId = _companyId,
                FullName = "rigon-org/api", DefaultBranch = "main",
            };
            var web = new GitHubRepository
            {
                RepositoryId = 556, GitHubInstallationId = installation.Id, CompanyId = _companyId,
                FullName = "rigon-org/web", DefaultBranch = "develop",
            };
            db.GitHubRepositories.AddRange(api, web);
            await db.SaveChangesAsync();
            _apiRepositoryId = api.Id;
            _webRepositoryId = web.Id;

            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                CompanyId = _companyId, ProjectId = _projectId, GitHubRepositoryId = _apiRepositoryId,
            });
            await db.SaveChangesAsync();
            token = (await h.Agents.RegenerateTokenAsync(human.Id)).Value!.Token;
        }

        _host = await McpTestHost.StartAsync(ConnectionString, s => s.AddSingleton<IGitHubApiClient>(_github));
        _client = await _host.ConnectAsync(token);
    }

    public async SystemTask.Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _host.DisposeAsync();
    }

    private async Task<(bool IsError, string Text)> Call(string tool, Dictionary<string, object?> args)
    {
        var result = await _client.CallToolAsync(tool, args);
        return (result.IsError == true, string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }

    private async Task<string> CreateTaskAsync(string title)
    {
        var (isError, text) = await Call("create_task", new() { ["project"] = "TS", ["title"] = title });
        Assert.False(isError, text);
        return text.Split(' ')[1].TrimEnd(':'); // "Created TS-1: title"
    }

    private async SystemTask.Task LinkWebAsync()
    {
        await using var db = NewContext();
        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            CompanyId = _companyId, ProjectId = _projectId, GitHubRepositoryId = _webRepositoryId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Everything written to the audit queue since the last drain.</summary>
    private async Task<List<AuditEntry>> DrainAudit()
    {
        var queue = _host.App.Services.GetRequiredService<AuditQueue>();
        var entries = new List<AuditEntry>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try { await foreach (var e in queue.ReadAllAsync(cts.Token)) entries.Add(e); }
        catch (OperationCanceledException) { }
        return entries;
    }

    [Fact]
    public async SystemTask.Task With_one_linked_repository_and_no_name_it_creates_the_suggested_branch_and_links_it()
    {
        var key = await CreateTaskAsync("Check for bugs at the repo");
        _github
            .OnGet("git/ref/heads/main", RefBody)
            .OnPost("git/refs", "{}");

        var (isError, text) = await Call("create_branch", new() { ["key"] = key });

        Assert.False(isError, text);
        Assert.Equal(
            $"Created branch {key}/check-for-bugs-at-the-repo in rigon-org/api for {key}: https://github.com/rigon-org/api/tree/{key}/check-for-bugs-at-the-repo",
            text);

        var post = Assert.Single(_github.Posts);
        Assert.Contains("repos/rigon-org/api/git/refs", post.Url);
        Assert.Contains($"refs/heads/{key}/check-for-bugs-at-the-repo", post.Body);

        await using var verify = NewContext();
        var branch = await verify.GitHubBranches.SingleAsync();
        var link = await verify.TaskLinks.SingleAsync();
        Assert.Equal(branch.Id, link.GitHubBranchId);
        Assert.Equal(int.Parse(key.Split('-')[1]), (await verify.Tasks.SingleAsync(t => t.Id == link.TaskId)).Number);
    }

    [Fact]
    public async SystemTask.Task An_explicit_name_with_the_key_is_used_as_given()
    {
        var key = await CreateTaskAsync("Test per branch");
        _github.OnGet("git/ref/heads/main", RefBody).OnPost("git/refs", "{}");

        var (isError, text) = await Call("create_branch", new() { ["key"] = key, ["name"] = $" {key}/Test-per-branch " });

        Assert.False(isError, text);
        Assert.Contains($"refs/heads/{key}/Test-per-branch", Assert.Single(_github.Posts).Body);
    }

    [Fact]
    public async SystemTask.Task A_name_without_the_key_is_refused_before_github_is_written()
    {
        var key = await CreateTaskAsync("Keyless");
        _github.OnGet("git/ref/heads/main", RefBody).OnPost("git/refs", "{}");

        var (isError, text) = await Call("create_branch", new() { ["key"] = key, ["name"] = "fix/keyless" });

        Assert.True(isError);
        Assert.Contains($"has to contain {key}", text);
        Assert.Empty(_github.Posts);
    }

    [Fact]
    public async SystemTask.Task With_two_linked_repositories_the_named_one_gets_the_branch()
    {
        await LinkWebAsync();
        var key = await CreateTaskAsync("Web work");
        _github.OnGet("git/ref/heads/develop", "{\"object\":{\"sha\":\"devsha\"}}").OnPost("git/refs", "{}");

        var (isError, text) = await Call("create_branch", new() { ["key"] = key, ["repository"] = "web" });

        Assert.False(isError, text);
        Assert.Contains("repos/rigon-org/web/git/refs", Assert.Single(_github.Posts).Url);
        Assert.Contains("in rigon-org/web", text);
    }

    [Fact]
    public async SystemTask.Task With_two_linked_repositories_and_none_named_it_lists_them()
    {
        await LinkWebAsync();
        var key = await CreateTaskAsync("Which repo");

        var (isError, text) = await Call("create_branch", new() { ["key"] = key });

        Assert.True(isError);
        Assert.Equal(
            $"{key}'s project links more than one repository; pass repository. Linked repositories: rigon-org/api, rigon-org/web.",
            text);
        Assert.Empty(_github.Posts);
    }

    [Fact]
    public async SystemTask.Task A_branch_that_already_exists_is_reported_as_success()
    {
        var key = await CreateTaskAsync("Exists");
        var branchName = $"{key}/exists";
        _github
            .OnGet("git/ref/heads/main", RefBody)
            .OnGet($"git/ref/heads/{branchName}", "{\"object\":{\"sha\":\"branchsha\"}}")
            .FailPost("git/refs", new Error(
                "GitHub.UnprocessableEntity",
                "GitHub returned 422 for https://api.github.com/repos/rigon-org/api/git/refs. Reference already exists"));

        var (isError, text) = await Call("create_branch", new() { ["key"] = key });

        Assert.False(isError, text);
        Assert.Equal(
            $"Branch {branchName} already existed in rigon-org/api; it is linked to {key}: https://github.com/rigon-org/api/tree/{branchName}",
            text);
    }

    [Fact]
    public async SystemTask.Task A_task_in_a_project_the_agent_is_not_in_is_refused_and_github_is_untouched()
    {
        // Seeded directly: the agent cannot create a task in OT through MCP.
        await using (var db = NewContext())
        {
            db.Tasks.Add(new TaskSphere.Domain.Entities.Task
            {
                Title = "Not yours", Number = 1, ProjectId = _otherProjectId, CompanyId = _companyId,
            });
            await db.SaveChangesAsync();
        }
        await DrainAudit();

        var (isError, _) = await Call("create_branch", new() { ["key"] = "OT-1" });

        Assert.True(isError);
        Assert.Empty(_github.Posts);
        Assert.Empty(_github.GetUrls);
        Assert.Equal(403, Assert.Single(await DrainAudit()).StatusCode);
    }

    [Fact]
    public async SystemTask.Task Success_and_failure_are_both_audited_as_create_branch()
    {
        var key = await CreateTaskAsync("Audited");
        _github.OnGet("git/ref/heads/main", RefBody).OnPost("git/refs", "{}");
        await DrainAudit();

        await Call("create_branch", new() { ["key"] = key });
        await Call("create_branch", new() { ["key"] = key, ["name"] = "no-key-here" });

        var entries = await DrainAudit();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.Equal("Created a branch", e.Action);
            Assert.Equal("/mcp/create_branch", e.Path);
            Assert.Equal("Rigon's Claude", e.Username);
        });
        Assert.Equal(200, entries[0].StatusCode);
        Assert.Equal(400, entries[1].StatusCode);
    }
}
