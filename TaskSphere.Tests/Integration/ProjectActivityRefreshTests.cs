using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Member = TaskSphere.Domain.Entities.Member;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class ProjectActivityRefreshTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereProjectRefreshTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _tsProjectId;       // key "TS", AutoDoneOnMerge = true, linked to api
    private int _optOutProjectId;   // key "OO", AutoDoneOnMerge = false, linked to web
    private int _apiRepositoryId;
    private int _webRepositoryId;
    private int _ts42TaskId;

    private const string MemberUserId = "member-1";
    private const string StrangerUserId = "stranger-1";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Answers every pull-request listing with one merged pull request whose head branch names
    /// TS-42, and records the urls it was asked for so a test can assert that NO call was made.
    /// </summary>
    private sealed class FakeGitHubApiClient : IGitHubApiClient
    {
        public List<string> Calls { get; } = new();
        public bool Fail { get; set; }

        public SystemTask.Task<Result<GitHubResponse>> GetAsync(
            long installationId, string url, CancellationToken cancellationToken = default)
        {
            Calls.Add(url);

            if (Fail)
                return SystemTask.Task.FromResult(
                    Result<GitHubResponse>.Failure(new Error("GitHub.Failed", "GitHub returned 500.")));

            var body = JsonSerializer.Serialize(new[]
            {
                new
                {
                    number = 7,
                    title = "Add the panel",
                    body = (string?)null,
                    state = "closed",
                    user = new { login = "rigon" },
                    head = new { @ref = "TS-42/add-the-panel" },
                    created_at = DateTime.UtcNow.AddDays(-1),
                    updated_at = DateTime.UtcNow,
                    merged_at = (DateTime?)DateTime.UtcNow,
                    html_url = "https://github.com/rigon-org/api/pull/7",
                },
            });

            return SystemTask.Task.FromResult(
                Result<GitHubResponse>.Success(new GitHubResponse(body, null)));
        }

        public SystemTask.Task<Result<GitHubResponse>> PostAsync(
            long installationId, string url, string jsonBody, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private static ProjectActivityRefreshService NewService(
        ApplicationDbContext db, FakeGitHubApiClient api)
    {
        var uow = new UnitOfWork(db);

        return new ProjectActivityRefreshService(
            uow,
            new AccessControlService(db),
            new GitHubPullRequestMirror(api, uow),
            new MergeTransitionService(uow, new AuditQueue()));
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Refresh Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        // DECOY ROWS — Projects, Repositories, Tasks and links must not share identity values,
        // or a lookup passing the wrong entity's id resolves correctly by accident.
        db.Projects.AddRange(
            new Project { Name = "Decoy A", Key = "DECA", CompanyId = _companyId },
            new Project { Name = "Decoy B", Key = "DECB", CompanyId = _companyId },
            new Project { Name = "Decoy C", Key = "DECC", CompanyId = _companyId });
        await db.SaveChangesAsync();

        var ts = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId, AutoDoneOnMerge = true };
        var oo = new Project { Name = "Opted Out", Key = "OO", CompanyId = _companyId, AutoDoneOnMerge = false };
        db.Projects.AddRange(ts, oo);
        await db.SaveChangesAsync();
        _tsProjectId = ts.Id;
        _optOutProjectId = oo.Id;

        db.Users.Add(new AppUser
        {
            Id = MemberUserId,
            UserName = "member@x.io",
            Email = "member@x.io",
            Name = "Member",
            CompanyId = _companyId
        });
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 12001,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        // DECOY REPOSITORY ROWS — Repositories must not collide with projects or tasks on identity.
        // Eleven decoys ensure real repositories start well beyond all project ids (1-5) and task ids.
        db.GitHubRepositories.AddRange(
            new GitHubRepository
            {
                RepositoryId = 12001,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-1",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12002,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-2",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12003,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-3",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12004,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-4",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12005,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-5",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12006,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-6",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12007,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-7",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12008,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-8",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12009,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-9",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12010,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-10",
                DefaultBranch = "main",
            },
            new GitHubRepository
            {
                RepositoryId = 12011,
                GitHubInstallationId = installation.Id,
                CompanyId = _companyId,
                FullName = "rigon-org/decoy-11",
                DefaultBranch = "main",
            });
        await db.SaveChangesAsync();

        var api = new GitHubRepository
        {
            RepositoryId = 12101,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var web = new GitHubRepository
        {
            RepositoryId = 12102,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/web",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.AddRange(api, web);
        await db.SaveChangesAsync();
        _apiRepositoryId = api.Id;
        _webRepositoryId = web.Id;

        db.ProjectRepositoryLinks.AddRange(
            new ProjectRepositoryLink
            {
                ProjectId = _tsProjectId,
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            },
            new ProjectRepositoryLink
            {
                ProjectId = _optOutProjectId,
                GitHubRepositoryId = _webRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
        await db.SaveChangesAsync();

        db.Members.Add(new Member
        {
            ProjectId = _tsProjectId,
            UserId = MemberUserId,
        });
        await db.SaveChangesAsync();

        // DECOY TASK ROWS — Tasks must not collide with repositories or projects on identity.
        // Eight decoys on a decoy project (Decoy A, id 1) ensure the real task starts after all
        // project ids (1-5) and repository ids (1-13).
        db.Set<TaskEntity>().AddRange(
            new TaskEntity
            {
                Title = "Decoy Task 1",
                Number = 1,
                ProjectId = 1,  // Decoy A project
                CompanyId = _companyId,
                Status = TaskStatuses.InProgress,
            },
            new TaskEntity
            {
                Title = "Decoy Task 2",
                Number = 2,
                ProjectId = 1,  // Decoy A project
                CompanyId = _companyId,
                Status = TaskStatuses.InProgress,
            },
            new TaskEntity
            {
                Title = "Decoy Task 3",
                Number = 3,
                ProjectId = 1,  // Decoy A project
                CompanyId = _companyId,
                Status = TaskStatuses.InProgress,
            },
            new TaskEntity
            {
                Title = "Decoy Task 4",
                Number = 4,
                ProjectId = 1,  // Decoy A project
                CompanyId = _companyId,
                Status = TaskStatuses.InProgress,
            },
            new TaskEntity
            {
                Title = "Decoy Task 5",
                Number = 5,
                ProjectId = 1,  // Decoy A project
                CompanyId = _companyId,
                Status = TaskStatuses.InProgress,
            },
            new TaskEntity
            {
                Title = "Decoy Task 6",
                Number = 6,
                ProjectId = 1,  // Decoy A project
                CompanyId = _companyId,
                Status = TaskStatuses.InProgress,
            },
            new TaskEntity
            {
                Title = "Decoy Task 7",
                Number = 7,
                ProjectId = 1,  // Decoy A project
                CompanyId = _companyId,
                Status = TaskStatuses.InProgress,
            },
            new TaskEntity
            {
                Title = "Decoy Task 8",
                Number = 8,
                ProjectId = 1,  // Decoy A project
                CompanyId = _companyId,
                Status = TaskStatuses.InProgress,
            });
        await db.SaveChangesAsync();

        var ts42 = new TaskEntity
        {
            Title = "Panel",
            Number = 42,
            ProjectId = _tsProjectId,
            CompanyId = _companyId,
            Status = TaskStatuses.InProgress,
        };
        db.Set<TaskEntity>().AddRange(ts42);
        await db.SaveChangesAsync();
        _ts42TaskId = ts42.Id;

        // SELF-CHECK: Verify the 5 fixture ids are pairwise distinct. This catches bugs where decoy
        // rows fail to offset identity seeds, allowing a lookup using the wrong entity id to
        // resolve correctly by accident. E.g., if _apiRepositoryId (3) == _ts42TaskId (3), a bug
        // that passed a repository id where a task id belongs would still find the correct row.
        var fixtureIds = new[] { _tsProjectId, _optOutProjectId, _apiRepositoryId, _webRepositoryId, _ts42TaskId };
        var uniqueIds = new HashSet<int>(fixtureIds);
        if (uniqueIds.Count != fixtureIds.Length)
        {
            var duplicates = fixtureIds
                .GroupBy(x => x)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            throw new InvalidOperationException(
                $"Fixture id collision: {string.Join(", ", duplicates)} appear in multiple fixture variables. " +
                $"_tsProjectId={_tsProjectId}, _optOutProjectId={_optOutProjectId}, " +
                $"_apiRepositoryId={_apiRepositoryId}, _webRepositoryId={_webRepositoryId}, " +
                $"_ts42TaskId={_ts42TaskId}");
        }
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private async SystemTask.Task<string> StatusOf(int taskId)
    {
        await using var db = NewContext();
        var task = await db.Set<TaskEntity>().SingleAsync(t => t.Id == taskId);
        return task.Status;
    }

    [Fact]
    public async SystemTask.Task Refreshes_the_projects_repositories_and_moves_the_merged_task()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Refreshed);
        Assert.Equal(1, result.Value.RepositoriesRefreshed);
        Assert.Equal(1, result.Value.TasksTransitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));

        // Only this project's repository, and only the pull-request listing: the commits pass
        // is what makes a full sync expensive, and this feature never pays for it.
        Assert.Single(api.Calls);
        Assert.Contains("/repos/rigon-org/api/pulls", api.Calls[0]);
    }

    [Fact]
    public async SystemTask.Task Stamps_the_cooldown_on_the_repositories_it_refreshed()
    {
        var api = new FakeGitHubApiClient();

        await using (var db = NewContext())
            await NewService(db, api).RefreshAsync(
                _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        await using var check = NewContext();
        var repository = await check.GitHubRepositories.SingleAsync(r => r.Id == _apiRepositoryId);

        Assert.NotNull(repository.PullRequestsRefreshedAtUtc);
    }

    [Fact]
    public async SystemTask.Task A_second_refresh_inside_the_window_makes_no_call()
    {
        var api = new FakeGitHubApiClient();

        await using (var first = NewContext())
            await NewService(first, api).RefreshAsync(
                _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.Single(api.Calls);

        await using var second = NewContext();
        var result = await NewService(second, api).RefreshAsync(
            _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        // The whole point of the cooldown: five people opening boards in the same minute cost
        // one call, not five.
        Assert.Single(api.Calls);
        Assert.False(result.Value!.Refreshed);
        Assert.Equal(0, result.Value.RepositoriesRefreshed);
    }

    [Fact]
    public async SystemTask.Task A_refresh_past_the_window_calls_again()
    {
        var api = new FakeGitHubApiClient();

        await using (var first = NewContext())
            await NewService(first, api).RefreshAsync(
                _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        // Age the stamp rather than waiting a minute in a test.
        await using (var age = NewContext())
        {
            var repository = await age.GitHubRepositories.SingleAsync(r => r.Id == _apiRepositoryId);
            repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await age.SaveChangesAsync();
        }

        await using var second = NewContext();
        var result = await NewService(second, api).RefreshAsync(
            _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.Equal(2, api.Calls.Count);
        Assert.True(result.Value!.Refreshed);
    }

    [Fact]
    public async SystemTask.Task An_opted_out_project_costs_no_github_call_at_all()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, _optOutProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Refreshed);
        // Asserted on the client's call list, not just the counts: the point of skipping early
        // is that the rate limit is never spent where it cannot buy anything.
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async SystemTask.Task A_project_with_no_linked_repository_costs_no_github_call()
    {
        var api = new FakeGitHubApiClient();

        int unlinkedProjectId;
        await using (var seed = NewContext())
        {
            var unlinked = new Project
            {
                Name = "Unlinked",
                Key = "UL",
                CompanyId = _companyId,
                AutoDoneOnMerge = true,
            };
            seed.Projects.Add(unlinked);
            await seed.SaveChangesAsync();
            unlinkedProjectId = unlinked.Id;
        }

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, unlinkedProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        // The toggle being on is not enough: with nothing linked there is nothing to fetch.
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Refreshed);
        Assert.Empty(api.Calls);
    }
}
