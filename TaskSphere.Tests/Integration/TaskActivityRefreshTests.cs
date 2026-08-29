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
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubCommit = TaskSphere.Domain.Entities.GitHubCommit;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Member = TaskSphere.Domain.Entities.Member;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class TaskActivityRefreshTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereTaskRefreshTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _tsProjectId;       // key "TS", linked to api
    private int _unlinkedProjectId; // key "UL", links no repositories
    private int _apiRepositoryId;
    private int _ts42TaskId;        // on TS, project links a repository
    private int _unlinkedTaskId;    // on UL, project links no repositories

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
    /// Answers /branches, /commits and /pulls URLs with one well-formed entry each, and
    /// records every requested URL so a test can assert that NO call was made or that
    /// specific URLs were.
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

            if (url.Contains("/branches"))
            {
                var branchBody = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        name = "TS-42/add-the-panel",
                        commit = new { sha = "abc123def456" },
                    },
                });

                return SystemTask.Task.FromResult(
                    Result<GitHubResponse>.Success(new GitHubResponse(branchBody, null)));
            }

            if (url.Contains("/commits"))
            {
                var commitBody = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        sha = "abc123def456",
                        commit = new
                        {
                            message = "Add the panel",
                            author = new { name = "Rigon", date = DateTime.UtcNow.AddHours(-1) },
                        },
                        author = new { login = "rigon" },
                        html_url = "https://github.com/rigon-org/api/commit/abc123def456",
                    },
                });

                return SystemTask.Task.FromResult(
                    Result<GitHubResponse>.Success(new GitHubResponse(commitBody, null)));
            }

            var pullBody = JsonSerializer.Serialize(new[]
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
                Result<GitHubResponse>.Success(new GitHubResponse(pullBody, null)));
        }

        public SystemTask.Task<Result<GitHubResponse>> PostAsync(
            long installationId, string url, string jsonBody, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private static TaskActivityRefreshService NewService(
        ApplicationDbContext db, FakeGitHubApiClient api)
    {
        var uow = new UnitOfWork(db);

        return new TaskActivityRefreshService(
            uow,
            new AccessControlService(db),
            new GitHubBranchMirror(api, uow),
            new GitHubCommitMirror(api, uow),
            new GitHubPullRequestMirror(api, uow),
            new MergeTransitionService(uow, new AuditQueue()),
            new GitHubTaskLinkResolver(uow));
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Task Refresh Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        // DECOY ROWS — Projects, Repositories, Branches, Commits and Tasks must not share
        // identity values, or a lookup passing the wrong entity's id resolves correctly by
        // accident.
        db.Projects.AddRange(
            new Project { Name = "Decoy A", Key = "DECA", CompanyId = _companyId },
            new Project { Name = "Decoy B", Key = "DECB", CompanyId = _companyId },
            new Project { Name = "Decoy C", Key = "DECC", CompanyId = _companyId });
        await db.SaveChangesAsync();

        var ts = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId, AutoDoneOnMerge = true };
        var ul = new Project { Name = "Unlinked", Key = "UL", CompanyId = _companyId, AutoDoneOnMerge = true };
        db.Projects.AddRange(ts, ul);
        await db.SaveChangesAsync();
        _tsProjectId = ts.Id;
        _unlinkedProjectId = ul.Id;

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
        db.GitHubRepositories.Add(api);
        await db.SaveChangesAsync();
        _apiRepositoryId = api.Id;

        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            ProjectId = _tsProjectId,
            GitHubRepositoryId = _apiRepositoryId,
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

        // DECOY BRANCH ROWS — Branches must not collide with repositories, projects or tasks
        // on identity.
        db.GitHubBranches.AddRange(
            new GitHubBranch
            {
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                Name = "decoy-branch-1",
                HeadSha = "decoy1",
            },
            new GitHubBranch
            {
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                Name = "decoy-branch-2",
                HeadSha = "decoy2",
            },
            new GitHubBranch
            {
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                Name = "decoy-branch-3",
                HeadSha = "decoy3",
            });
        await db.SaveChangesAsync();

        // DECOY COMMIT ROWS — Commits must not collide with branches, repositories, projects
        // or tasks on identity.
        db.GitHubCommits.AddRange(
            new GitHubCommit
            {
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                Sha = "decoycommit1",
                Message = "Decoy commit 1",
                AuthorName = "Decoy",
                CommittedAtUtc = DateTime.UtcNow.AddDays(-10),
                HtmlUrl = "https://github.com/rigon-org/api/commit/decoycommit1",
            },
            new GitHubCommit
            {
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                Sha = "decoycommit2",
                Message = "Decoy commit 2",
                AuthorName = "Decoy",
                CommittedAtUtc = DateTime.UtcNow.AddDays(-9),
                HtmlUrl = "https://github.com/rigon-org/api/commit/decoycommit2",
            },
            new GitHubCommit
            {
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                Sha = "decoycommit3",
                Message = "Decoy commit 3",
                AuthorName = "Decoy",
                CommittedAtUtc = DateTime.UtcNow.AddDays(-8),
                HtmlUrl = "https://github.com/rigon-org/api/commit/decoycommit3",
            });
        await db.SaveChangesAsync();

        // DECOY TASK ROWS — Tasks must not collide with repositories, branches, commits or
        // projects on identity.
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
        var unlinked = new TaskEntity
        {
            Title = "Orphan",
            Number = 1,
            ProjectId = _unlinkedProjectId,
            CompanyId = _companyId,
            Status = TaskStatuses.InProgress,
        };
        db.Set<TaskEntity>().AddRange(ts42, unlinked);
        await db.SaveChangesAsync();
        _ts42TaskId = ts42.Id;
        _unlinkedTaskId = unlinked.Id;

        // SELF-CHECK: Verify the fixture ids are pairwise distinct. This catches bugs where
        // decoy rows fail to offset identity seeds, allowing a lookup using the wrong entity id
        // to resolve correctly by accident.
        var fixtureIds = new[]
        {
            _tsProjectId, _unlinkedProjectId, _apiRepositoryId, _ts42TaskId, _unlinkedTaskId,
        };
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
                $"_tsProjectId={_tsProjectId}, _unlinkedProjectId={_unlinkedProjectId}, " +
                $"_apiRepositoryId={_apiRepositoryId}, _ts42TaskId={_ts42TaskId}, " +
                $"_unlinkedTaskId={_unlinkedTaskId}");
        }
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task ANonMember_IsForbidden_AndNoGitHubCallIsMade()
    {
        await using var db = NewContext();
        var api = new FakeGitHubApiClient();
        var service = NewService(db, api);

        var result = await service.RefreshAsync(
            _companyId, _ts42TaskId, StrangerUserId, isCompanyAdmin: false, actorUsername: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async SystemTask.Task AMissingTask_ReadsAsForbidden_ToANonMember()
    {
        await using var db = NewContext();
        var api = new FakeGitHubApiClient();
        var service = NewService(db, api);

        // 999999 does not exist. A non-member must not be able to tell that apart from a task
        // they simply cannot see — the same rule GitHubTaskActivityService.GetForTaskAsync follows.
        var result = await service.RefreshAsync(
            _companyId, 999999, StrangerUserId, isCompanyAdmin: false, actorUsername: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async SystemTask.Task AdminOnAMissingTask_GetsNotFound()
    {
        await using var db = NewContext();
        var api = new FakeGitHubApiClient();
        var service = NewService(db, api);

        var result = await service.RefreshAsync(
            _companyId, 999999, MemberUserId, isCompanyAdmin: true, actorUsername: null);

        Assert.False(result.IsSuccess);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async SystemTask.Task ATaskWhoseProjectLinksNoRepositories_IsQuiet()
    {
        await using var db = NewContext();
        var api = new FakeGitHubApiClient();
        var service = NewService(db, api);

        var result = await service.RefreshAsync(
            _companyId, _unlinkedTaskId, MemberUserId, isCompanyAdmin: true, actorUsername: null);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Refreshed);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async SystemTask.Task ARepositoryInsideBothCooldowns_IsNotCalledAtAll()
    {
        await using var seed = NewContext();
        var repository = await seed.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
        repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-5);
        repository.CommitsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-5);
        await seed.SaveChangesAsync();

        await using var db = NewContext();
        var api = new FakeGitHubApiClient();
        var service = NewService(db, api);

        var result = await service.RefreshAsync(
            _companyId, _ts42TaskId, MemberUserId, isCompanyAdmin: true, actorUsername: null);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Refreshed);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async SystemTask.Task CommitsDue_ButPullsNot_CallsBranchesAndCommits_NotPulls()
    {
        await using var seed = NewContext();
        var repository = await seed.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
        repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-5);   // inside 60s
        repository.CommitsRefreshedAtUtc = DateTime.UtcNow.AddMinutes(-10);       // outside 5min
        await seed.SaveChangesAsync();

        await using var db = NewContext();
        var api = new FakeGitHubApiClient();
        var service = NewService(db, api);

        await service.RefreshAsync(_companyId, _ts42TaskId, MemberUserId, true, null);

        // Branches are fetched because the commits pass consumes the branch list, not because
        // pull requests are due.
        Assert.Contains(api.Calls, c => c.Contains("/branches"));
        Assert.Contains(api.Calls, c => c.Contains("/commits?sha="));
        Assert.DoesNotContain(api.Calls, c => c.Contains("/pulls"));
    }

    [Fact]
    public async SystemTask.Task PullsDue_ButCommitsNot_SkipsTheCommitListings()
    {
        await using var seed = NewContext();
        var repository = await seed.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
        repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddMinutes(-2);   // outside 60s
        repository.CommitsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-30);       // inside 5min
        await seed.SaveChangesAsync();

        await using var db = NewContext();
        var api = new FakeGitHubApiClient();
        var service = NewService(db, api);

        await service.RefreshAsync(_companyId, _ts42TaskId, MemberUserId, true, null);

        Assert.Contains(api.Calls, c => c.Contains("/pulls"));
        Assert.DoesNotContain(api.Calls, c => c.Contains("/commits?sha="));
    }
}
