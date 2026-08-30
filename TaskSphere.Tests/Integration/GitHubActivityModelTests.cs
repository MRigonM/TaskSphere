using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

// Aliased, not imported: TaskSphere.Domain.Entities.Task shadows System.Threading.Tasks.Task.
using Company = TaskSphere.Domain.Entities.Company;
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubCommit = TaskSphere.Domain.Entities.GitHubCommit;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubPullRequest = TaskSphere.Domain.Entities.GitHubPullRequest;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using TaskLink = TaskSphere.Domain.Entities.TaskLink;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The three GitHub-identity indexes are unfiltered, following B1's rule: a branch that is
/// merged, deleted and later recreated must revive its row rather than sit beside it. The
/// TaskLink indexes are filtered, so a soft-deleted link never blocks a new one.
/// </summary>
public class GitHubActivityModelTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubActivityModelTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _repositoryId;
    private int _taskId;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Activity Model Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 7001,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.Selected,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 8001,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();
        _repositoryId = repository.Id;

        var task = new TaskEntity
        {
            Title = "Wire the activity panel",
            Number = 42,
            ProjectId = project.Id,
            CompanyId = _companyId,
        };
        db.Set<TaskEntity>().Add(task);
        await db.SaveChangesAsync();
        _taskId = task.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private GitHubBranch NewBranch(string name) => new()
    {
        GitHubRepositoryId = _repositoryId,
        CompanyId = _companyId,
        Name = name,
        HeadSha = "aaaaaaa",
    };

    [Fact]
    public async SystemTask.Task ActivitySyncedAtUtc_RoundTrips_AndIsNullUntilASyncRuns()
    {
        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            Assert.Null(installation.ActivitySyncedAtUtc);

            installation.ActivitySyncedAtUtc = new DateTime(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            Assert.Equal(new DateTime(2026, 8, 12, 9, 30, 0), installation.ActivitySyncedAtUtc);
        }
    }

    [Fact]
    public async SystemTask.Task DuplicateCommitSha_InTheSameRepository_IsRejected_EvenWhenSoftDeleted()
    {
        await using (var db = NewContext())
        {
            db.GitHubCommits.Add(new GitHubCommit
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Sha = "0123456789abcdef0123456789abcdef01234567",
                Message = "TS-42 first",
                AuthorName = "Rigon",
                CommittedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/rigon-org/api/commit/0123456",
                IsDeleted = true,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.GitHubCommits.Add(new GitHubCommit
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Sha = "0123456789abcdef0123456789abcdef01234567",
                Message = "TS-42 again",
                AuthorName = "Rigon",
                CommittedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/rigon-org/api/commit/0123456",
            });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_GitHubCommits_RepositoryId_Sha", ex.GetBaseException().Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async SystemTask.Task DeletedThenRecreatedBranch_CollidesOnTheUnfilteredIndex_SoUpsertsMustRevive()
    {
        await using (var db = NewContext())
        {
            var branch = NewBranch("TS-42-fix");
            branch.IsDeleted = true;
            branch.DeletedAt = DateTime.UtcNow;
            db.GitHubBranches.Add(branch);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.GitHubBranches.Add(NewBranch("TS-42-fix"));

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_GitHubBranches_RepositoryId_Name", ex.GetBaseException().Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async SystemTask.Task DuplicatePullRequestNumber_InTheSameRepository_IsRejected()
    {
        // Soft-deleted first, matching the commit and branch tests above: a closed-then-reopened
        // PR reuses its number, so a filtered index would wrongly admit the duplicate here.
        await using (var db = NewContext())
        {
            var pull = NewPullRequest(17);
            pull.IsDeleted = true;
            pull.DeletedAt = DateTime.UtcNow;
            db.GitHubPullRequests.Add(pull);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.GitHubPullRequests.Add(NewPullRequest(17));

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_GitHubPullRequests_RepositoryId_Number", ex.GetBaseException().Message, StringComparison.Ordinal);
        }

        GitHubPullRequest NewPullRequest(int number) => new()
        {
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            Number = number,
            Title = "TS-42 wire the panel",
            State = PullRequestState.Open,
            AuthorLogin = "MRigonM",
            HeadBranch = "TS-42-fix",
            OpenedAtUtc = DateTime.UtcNow,
            GitHubUpdatedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/pull/17",
        };
    }

    [Fact]
    public async SystemTask.Task UnlinkedThenRelinkedTaskLink_IsAllowed_BecauseTheLinkIndexIsFiltered()
    {
        int commitId;

        await using (var db = NewContext())
        {
            var commit = new GitHubCommit
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Sha = "fedcba9876543210fedcba9876543210fedcba98",
                Message = "TS-42 done",
                AuthorName = "Rigon",
                CommittedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/rigon-org/api/commit/fedcba9",
            };
            db.GitHubCommits.Add(commit);
            await db.SaveChangesAsync();
            commitId = commit.Id;

            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = commitId });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            // A second *live* link to the same commit must be rejected: the resolver in
            // Tasks 4-5 writes on every sync, so a re-run must not create a second live row.
            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = commitId });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_TaskLinks_TaskId_CommitId", ex.GetBaseException().Message, StringComparison.Ordinal);
        }

        await using (var db = NewContext())
        {
            var link = await db.TaskLinks.SingleAsync();
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = commitId });
            await db.SaveChangesAsync();

            Assert.Single(await db.TaskLinks.ToListAsync());
            Assert.Equal(2, await db.TaskLinks.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public async SystemTask.Task UnlinkedThenRelinkedBranchTaskLink_IsAllowed_ButADuplicateLiveLinkIsRejected()
    {
        // Mirrors UnlinkedThenRelinkedTaskLink_… for the GitHubBranchId FK, so
        // IX_TaskLinks_TaskId_BranchId's own IsDeleted clause is pinned, not just the commit one.
        int branchId;

        await using (var db = NewContext())
        {
            var branch = NewBranch("TS-42-fix");
            db.GitHubBranches.Add(branch);
            await db.SaveChangesAsync();
            branchId = branch.Id;

            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branchId });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branchId });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_TaskLinks_TaskId_BranchId", ex.GetBaseException().Message, StringComparison.Ordinal);
        }

        await using (var db = NewContext())
        {
            var link = await db.TaskLinks.SingleAsync();
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branchId });
            await db.SaveChangesAsync();

            Assert.Single(await db.TaskLinks.ToListAsync());
            Assert.Equal(2, await db.TaskLinks.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public async SystemTask.Task UnlinkedThenRelinkedPullRequestTaskLink_IsAllowed_ButADuplicateLiveLinkIsRejected()
    {
        // Mirrors UnlinkedThenRelinkedTaskLink_… for the GitHubPullRequestId FK, so
        // IX_TaskLinks_TaskId_PullRequestId's own IsDeleted clause is pinned too.
        int pullRequestId;

        await using (var db = NewContext())
        {
            var pull = new GitHubPullRequest
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Number = 19,
                Title = "TS-42 wire the panel",
                State = PullRequestState.Open,
                AuthorLogin = "MRigonM",
                HeadBranch = "TS-42-fix",
                OpenedAtUtc = DateTime.UtcNow,
                GitHubUpdatedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/rigon-org/api/pull/19",
            };
            db.GitHubPullRequests.Add(pull);
            await db.SaveChangesAsync();
            pullRequestId = pull.Id;

            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubPullRequestId = pullRequestId });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubPullRequestId = pullRequestId });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_TaskLinks_TaskId_PullRequestId", ex.GetBaseException().Message, StringComparison.Ordinal);
        }

        await using (var db = NewContext())
        {
            var link = await db.TaskLinks.SingleAsync();
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubPullRequestId = pullRequestId });
            await db.SaveChangesAsync();

            Assert.Single(await db.TaskLinks.ToListAsync());
            Assert.Equal(2, await db.TaskLinks.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public async SystemTask.Task TwoLinksFromTheSameTask_ToDifferentRecordKinds_Coexist()
    {
        // The three filtered indexes are per-kind, so a task linked to both a branch and a
        // pull request is normal traffic, not a collision.
        await using var db = NewContext();

        var branch = NewBranch("TS-42-fix");
        db.GitHubBranches.Add(branch);

        var pull = new GitHubPullRequest
        {
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            Number = 18,
            Title = "TS-42 wire the panel",
            State = PullRequestState.Merged,
            AuthorLogin = "MRigonM",
            HeadBranch = "TS-42-fix",
            OpenedAtUtc = DateTime.UtcNow,
            GitHubUpdatedAtUtc = DateTime.UtcNow,
            MergedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/pull/18",
        };
        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();

        db.TaskLinks.AddRange(
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branch.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubPullRequestId = pull.Id });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.TaskLinks.CountAsync());
    }

    [Fact]
    public async SystemTask.Task SoftDeletedCommit_IsHiddenByQueryFilter_AndVisibleWhenIgnored()
    {
        // Same reasoning as the branch and pull request variants below: the read surfaces
        // deleted records with a marker rather than dropping them, so it must suppress this
        // filter deliberately, for all three record kinds, not just branches.
        await using (var db = NewContext())
        {
            db.GitHubCommits.Add(new GitHubCommit
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Sha = "2222222222222222222222222222222222222b",
                Message = "TS-42 filter check",
                AuthorName = "Rigon",
                CommittedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/rigon-org/api/commit/2222222",
            });
            await db.SaveChangesAsync();

            var commit = await db.GitHubCommits.SingleAsync();
            commit.IsDeleted = true;
            commit.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            Assert.Empty(await db.GitHubCommits.ToListAsync());
            Assert.Single(await db.GitHubCommits.IgnoreQueryFilters().ToListAsync());
        }
    }

    [Fact]
    public async SystemTask.Task SoftDeletedBranch_IsHiddenByQueryFilter_AndVisibleWhenIgnored()
    {
        // The read surfaces deleted branches with a marker rather than dropping them, so it
        // must suppress this filter deliberately.
        await using (var db = NewContext())
        {
            db.GitHubBranches.Add(NewBranch("TS-42-fix"));
            await db.SaveChangesAsync();

            var branch = await db.GitHubBranches.SingleAsync();
            branch.IsDeleted = true;
            branch.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            Assert.Empty(await db.GitHubBranches.ToListAsync());
            Assert.Single(await db.GitHubBranches.IgnoreQueryFilters().ToListAsync());
        }
    }

    [Fact]
    public async SystemTask.Task SoftDeletedPullRequest_IsHiddenByQueryFilter_AndVisibleWhenIgnored()
    {
        // Same reasoning as SoftDeletedBranch_… above, for the pull request query filter.
        await using (var db = NewContext())
        {
            db.GitHubPullRequests.Add(new GitHubPullRequest
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Number = 21,
                Title = "TS-42 filter check",
                State = PullRequestState.Open,
                AuthorLogin = "MRigonM",
                HeadBranch = "TS-42-fix",
                OpenedAtUtc = DateTime.UtcNow,
                GitHubUpdatedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/rigon-org/api/pull/21",
            });
            await db.SaveChangesAsync();

            var pull = await db.GitHubPullRequests.SingleAsync();
            pull.IsDeleted = true;
            pull.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            Assert.Empty(await db.GitHubPullRequests.ToListAsync());
            Assert.Single(await db.GitHubPullRequests.IgnoreQueryFilters().ToListAsync());
        }
    }
}
