using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubCommit = TaskSphere.Domain.Entities.GitHubCommit;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubPullRequest = TaskSphere.Domain.Entities.GitHubPullRequest;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Member = TaskSphere.Domain.Entities.Member;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using TaskLink = TaskSphere.Domain.Entities.TaskLink;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The read re-checks the repo↔project link on every call, which is what makes unlinking a
/// repository hide its activity immediately with no cleanup job and no stale grants.
/// </summary>
public class GitHubTaskActivityReadTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubActivityReadTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const string MemberUserId = "member-user";
    private const string OutsiderUserId = "outsider-user";

    private Guid _companyId;
    private int _projectId;
    private int _repositoryId;
    private int _taskId;
    private int _linkId;

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

        var company = new Company { Name = "Read Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        db.Users.AddRange(
            new AppUser { Id = MemberUserId, UserName = "member@x.io", Email = "member@x.io", Name = "Member", CompanyId = _companyId },
            new AppUser { Id = OutsiderUserId, UserName = "out@x.io", Email = "out@x.io", Name = "Outsider", CompanyId = _companyId });
        await db.SaveChangesAsync();

        var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        db.Members.Add(new Member { ProjectId = _projectId, UserId = MemberUserId });
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 7401,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
            ActivitySyncedAtUtc = new DateTime(2026, 8, 12, 7, 0, 0, DateTimeKind.Utc),
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 8401,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();
        _repositoryId = repository.Id;

        var link = new ProjectRepositoryLink
        {
            ProjectId = _projectId,
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            LinkedByUserId = "rigon",
        };
        db.ProjectRepositoryLinks.Add(link);

        var task = new TaskEntity { Title = "Panel", Number = 42, ProjectId = _projectId, CompanyId = _companyId };
        db.Set<TaskEntity>().Add(task);
        await db.SaveChangesAsync();

        _linkId = link.Id;
        _taskId = task.Id;

        var commit = new GitHubCommit
        {
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            Sha = "1234567890abcdef1234567890abcdef12345678",
            Message = "TS-42 wire the panel\n\nWith a body.",
            AuthorName = "Rigon",
            AuthorLogin = "MRigonM",
            CommittedAtUtc = new DateTime(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc),
            HtmlUrl = "https://github.com/rigon-org/api/commit/1234567",
        };
        var branch = new GitHubBranch
        {
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            Name = "TS-42-fix",
            HeadSha = "abcdefg",
        };
        var pull = new GitHubPullRequest
        {
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            Number = 17,
            Title = "TS-42 wire the panel",
            State = PullRequestState.Merged,
            AuthorLogin = "MRigonM",
            HeadBranch = "TS-42-fix",
            OpenedAtUtc = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc),
            GitHubUpdatedAtUtc = new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc),
            MergedAtUtc = new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc),
            HtmlUrl = "https://github.com/rigon-org/api/pull/17",
        };
        db.GitHubCommits.Add(commit);
        db.GitHubBranches.Add(branch);
        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();

        db.TaskLinks.AddRange(
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = commit.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branch.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubPullRequestId = pull.Id });
        await db.SaveChangesAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private async SystemTask.Task<Result<TaskGitHubActivityDto>> Read(
        string userId = MemberUserId,
        bool isCompanyAdmin = false,
        int? taskId = null)
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        var access = new AccessControlService(db);

        return await new GitHubTaskActivityService(uow, access)
            .GetForTaskAsync(_companyId, userId, isCompanyAdmin, taskId ?? _taskId);
    }

    [Fact]
    public async SystemTask.Task AProjectMember_GetsTheTasksActivity()
    {
        var result = await Read();

        Assert.True(result.IsSuccess);

        var activity = result.Value!;

        var commit = Assert.Single(activity.Commits);
        Assert.Equal("1234567890abcdef1234567890abcdef12345678", commit.Sha);
        Assert.Equal("1234567", commit.ShortSha);
        Assert.StartsWith("TS-42 wire the panel", commit.Message, StringComparison.Ordinal);
        Assert.Equal("MRigonM", commit.AuthorLogin);
        Assert.Equal("rigon-org/api", commit.RepositoryFullName);

        var branch = Assert.Single(activity.Branches);
        Assert.Equal("TS-42-fix", branch.Name);
        Assert.False(branch.IsDeleted);

        var pull = Assert.Single(activity.PullRequests);
        Assert.Equal(17, pull.Number);
        Assert.Equal(PullRequestState.Merged, pull.State);

        Assert.Equal(new DateTime(2026, 8, 12, 7, 0, 0), activity.LastSyncedAtUtc);
    }

    [Fact]
    public async SystemTask.Task ANonMember_GetsForbidden_AndCannotTellAMissingTaskFromAForbiddenOne()
    {
        // The access check precedes the task lookup, per sub-project A's rule.
        var onRealTask = await Read(OutsiderUserId);
        var onMissingTask = await Read(OutsiderUserId, taskId: 999_999);

        Assert.False(onRealTask.IsSuccess);
        Assert.False(onMissingTask.IsSuccess);
        Assert.Equal("Auth.Forbidden", onRealTask.Errors[0].Code);
        Assert.Equal("Auth.Forbidden", onMissingTask.Errors[0].Code);
    }

    [Fact]
    public async SystemTask.Task ACompanyAdmin_BypassesMembership()
    {
        var result = await Read(OutsiderUserId, isCompanyAdmin: true);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Commits);
    }

    [Fact]
    public async SystemTask.Task AnAdminOnAMissingTask_GetsNotFound_NotAnEmptyPayload()
    {
        var result = await Read(OutsiderUserId, isCompanyAdmin: true, taskId: 999_999);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Errors[0].Code);
    }

    [Fact]
    public async SystemTask.Task UnlinkingTheRepository_HidesItsActivity_WithoutTouchingAnyRow()
    {
        await using (var db = NewContext())
        {
            var link = await db.ProjectRepositoryLinks.SingleAsync(l => l.Id == _linkId);
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var result = await Read();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Commits);
        Assert.Empty(result.Value.Branches);
        Assert.Empty(result.Value.PullRequests);

        await using (var db = NewContext())
        {
            // No cleanup pass: the rows survive, so re-linking restores the panel.
            Assert.Equal(3, await db.TaskLinks.CountAsync());
            Assert.Single(await db.GitHubCommits.ToListAsync());
        }
    }

    [Fact]
    public async SystemTask.Task ASoftDeletedBranch_IsStillRendered_WithItsMarkerSet()
    {
        await using (var db = NewContext())
        {
            var branchToDelete = await db.GitHubBranches.SingleAsync();
            branchToDelete.IsDeleted = true;
            branchToDelete.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var result = await Read();

        var branch = Assert.Single(result.Value!.Branches);

        Assert.Equal("TS-42-fix", branch.Name);
        Assert.True(branch.IsDeleted);
    }

    [Fact]
    public async SystemTask.Task ATaskWithNoLinks_IsAnEmptySuccess_NotAFailure()
    {
        int emptyTaskId;

        await using (var db = NewContext())
        {
            var task = new TaskEntity { Title = "Nothing yet", Number = 43, ProjectId = _projectId, CompanyId = _companyId };
            db.Set<TaskEntity>().Add(task);
            await db.SaveChangesAsync();
            emptyTaskId = task.Id;
        }

        var result = await Read(taskId: emptyTaskId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Commits);
        Assert.NotNull(result.Value.LastSyncedAtUtc);
    }

    [Fact]
    public async SystemTask.Task ACompanyWithNoInstallation_ReadsEmpty_RatherThanFailing()
    {
        // The modal cannot branch on connection state — a User-role member cannot call the
        // Company-gated connection endpoint — so the read must answer for everyone.
        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            installation.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var result = await Read();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.LastSyncedAtUtc);
    }

    [Fact]
    public async SystemTask.Task NothingFromAnotherCompanyIsReachable()
    {
        Guid otherCompanyId;

        await using (var db = NewContext())
        {
            var other = new Company { Name = "Other Co" };
            db.Companies.Add(other);
            await db.SaveChangesAsync();
            otherCompanyId = other.Id;
        }

        await using var db2 = NewContext();
        var result = await new GitHubTaskActivityService(new UnitOfWork(db2), new AccessControlService(db2))
            .GetForTaskAsync(otherCompanyId, MemberUserId, isCompanyAdmin: true, _taskId);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Errors[0].Code);
    }

    /// <summary>
    /// A second repository holding one record of each kind, every one of them linked to the
    /// fixture's task. <paramref name="linkedToProjectId"/> is the project it is linked to;
    /// null links it to no project at all. Every read test before this one seeded exactly one
    /// repository, where "filtered to the authorized repositories" and "not filtered" are
    /// indistinguishable.
    /// </summary>
    private async SystemTask.Task SeedASecondRepository(int? linkedToProjectId)
    {
        await using var db = NewContext();

        var installationId = (await db.GitHubInstallations.SingleAsync()).Id;

        var repository = new GitHubRepository
        {
            RepositoryId = 8402,
            GitHubInstallationId = installationId,
            CompanyId = _companyId,
            FullName = "rigon-org/other",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();

        if (linkedToProjectId is not null)
        {
            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                ProjectId = linkedToProjectId.Value,
                GitHubRepositoryId = repository.Id,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
        }

        var commit = new GitHubCommit
        {
            GitHubRepositoryId = repository.Id,
            CompanyId = _companyId,
            Sha = "fedcba0987654321fedcba0987654321fedcba09",
            Message = "TS-42 from the other repository",
            AuthorName = "Rigon",
            AuthorLogin = "MRigonM",
            CommittedAtUtc = new DateTime(2026, 8, 11, 11, 0, 0, DateTimeKind.Utc),
            HtmlUrl = "https://github.com/rigon-org/other/commit/fedcba0",
        };
        var branch = new GitHubBranch
        {
            GitHubRepositoryId = repository.Id,
            CompanyId = _companyId,
            Name = "TS-42-other",
            HeadSha = "fedcba0",
        };
        var pull = new GitHubPullRequest
        {
            GitHubRepositoryId = repository.Id,
            CompanyId = _companyId,
            Number = 18,
            Title = "TS-42 from the other repository",
            State = PullRequestState.Open,
            AuthorLogin = "MRigonM",
            HeadBranch = "TS-42-other",
            OpenedAtUtc = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc),
            GitHubUpdatedAtUtc = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc),
            HtmlUrl = "https://github.com/rigon-org/other/pull/18",
        };
        db.GitHubCommits.Add(commit);
        db.GitHubBranches.Add(branch);
        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();

        db.TaskLinks.AddRange(
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = commit.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branch.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubPullRequestId = pull.Id });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A second record of each kind in the repository that IS linked, so the three orderings
    /// are pinned. With one record apiece nothing distinguishes ascending from descending.
    /// </summary>
    private async SystemTask.Task SeedASecondRecordOfEachKind()
    {
        await using var db = NewContext();

        var commit = new GitHubCommit
        {
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            Sha = "0000000111111112222222233333333444444455",
            Message = "TS-42 the earlier commit",
            AuthorName = "Rigon",
            AuthorLogin = "MRigonM",
            CommittedAtUtc = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc),
            HtmlUrl = "https://github.com/rigon-org/api/commit/0000000",
        };
        var branch = new GitHubBranch
        {
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            Name = "AAA-sorts-first",
            HeadSha = "0000000",
        };
        var pull = new GitHubPullRequest
        {
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            Number = 16,
            Title = "TS-42 the earlier pull request",
            State = PullRequestState.Open,
            AuthorLogin = "MRigonM",
            HeadBranch = "AAA-sorts-first",
            OpenedAtUtc = new DateTime(2026, 8, 8, 9, 0, 0, DateTimeKind.Utc),
            GitHubUpdatedAtUtc = new DateTime(2026, 8, 8, 9, 0, 0, DateTimeKind.Utc),
            HtmlUrl = "https://github.com/rigon-org/api/pull/16",
        };
        db.GitHubCommits.Add(commit);
        db.GitHubBranches.Add(branch);
        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();

        db.TaskLinks.AddRange(
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = commit.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branch.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubPullRequestId = pull.Id });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async SystemTask.Task RecordsFromARepositoryLinkedToNoProject_AreNotRendered()
    {
        // The per-record authorization filter, which the unlink test cannot reach: there the
        // authorized set is empty and the read returns before the filter ever runs.
        await SeedASecondRepository(linkedToProjectId: null);

        var result = await Read();

        Assert.Equal("rigon-org/api", Assert.Single(result.Value!.Commits).RepositoryFullName);
        Assert.Equal("rigon-org/api", Assert.Single(result.Value.Branches).RepositoryFullName);
        Assert.Equal("rigon-org/api", Assert.Single(result.Value.PullRequests).RepositoryFullName);
    }

    [Fact]
    public async SystemTask.Task ARepositoryLinkedToAnotherProject_DoesNotAuthorizeThisTasksRecords()
    {
        // The link is looked up by project, not by company: another project's grant is not this
        // project's grant, or any member of any project could read every repository's activity.
        int otherProjectId;

        await using (var db = NewContext())
        {
            var other = new Project { Name = "Other", Key = "OT", CompanyId = _companyId };
            db.Projects.Add(other);
            await db.SaveChangesAsync();
            otherProjectId = other.Id;
        }

        await SeedASecondRepository(linkedToProjectId: otherProjectId);

        var result = await Read();

        Assert.Equal("rigon-org/api", Assert.Single(result.Value!.Commits).RepositoryFullName);
        Assert.Equal("rigon-org/api", Assert.Single(result.Value.Branches).RepositoryFullName);
        Assert.Equal("rigon-org/api", Assert.Single(result.Value.PullRequests).RepositoryFullName);
    }

    [Fact]
    public async SystemTask.Task Commits_AreNewestFirst()
    {
        await SeedASecondRecordOfEachKind();

        var result = await Read();

        Assert.Collection(
            result.Value!.Commits,
            c => Assert.Equal(new DateTime(2026, 8, 11, 10, 0, 0), c.CommittedAtUtc),
            c => Assert.Equal(new DateTime(2026, 8, 9, 10, 0, 0), c.CommittedAtUtc));
    }

    [Fact]
    public async SystemTask.Task Branches_AreAlphabetical()
    {
        await SeedASecondRecordOfEachKind();

        var result = await Read();

        Assert.Collection(
            result.Value!.Branches,
            b => Assert.Equal("AAA-sorts-first", b.Name),
            b => Assert.Equal("TS-42-fix", b.Name));
    }

    [Fact]
    public async SystemTask.Task PullRequests_AreNewestFirst()
    {
        await SeedASecondRecordOfEachKind();

        var result = await Read();

        Assert.Collection(
            result.Value!.PullRequests,
            p => Assert.Equal(17, p.Number),
            p => Assert.Equal(16, p.Number));
    }

    [Fact]
    public async SystemTask.Task ATaskWhoseProjectWasDeleted_ReadsEmpty_RatherThanThrowing()
    {
        // Reachable in production: the task -> project FK is OnDelete(SetNull), so deleting a
        // project leaves its tasks with no project. The links survive and must grant nothing,
        // because a null project has no repository links to re-check against.
        int orphanTaskId;

        await using (var db = NewContext())
        {
            var task = new TaskEntity { Title = "Orphan", Number = 44, ProjectId = null, CompanyId = _companyId };
            db.Set<TaskEntity>().Add(task);
            await db.SaveChangesAsync();
            orphanTaskId = task.Id;

            var commitId = (await db.GitHubCommits.SingleAsync()).Id;
            db.TaskLinks.Add(new TaskLink { CompanyId = _companyId, TaskId = orphanTaskId, GitHubCommitId = commitId });
            await db.SaveChangesAsync();
        }

        var result = await Read(OutsiderUserId, isCompanyAdmin: true, taskId: orphanTaskId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Commits);
    }

    [Fact]
    public async SystemTask.Task AnInheritedCommit_CarriesTheNameOfTheBranchThatConferredIt()
    {
        await using var db = NewContext();

        // Two commits on one task: one named the task, one was inherited. The panel has to tell
        // them apart, and the read must name WHICH branch — a bare flag could not.
        var branch = new TaskSphere.Domain.Entities.GitHubBranch
        {
            CompanyId = _companyId,
            GitHubRepositoryId = _repositoryId,
            Name = "TS-42-login",
            HeadSha = "bbb",
        };
        db.GitHubBranches.Add(branch);

        var direct = NewCommit("direct1", "TS-42 add the form");
        var inherited = NewCommit("ahead1", "wire up the login form");
        db.GitHubCommits.AddRange(direct, inherited);
        await db.SaveChangesAsync();

        db.TaskLinks.AddRange(
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branch.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = direct.Id },
            new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = inherited.Id, ViaGitHubBranchId = branch.Id });
        await db.SaveChangesAsync();

        var result = await Read(isCompanyAdmin: true);

        var commits = result.Value!.Commits;

        // The pairing, not the presence: assert each sha carries the right provenance. Asserting
        // that "TS-42-login" appears somewhere would pass even if it were on the wrong commit.
        // Single-with-predicate rather than indexing, because the fixture seeds records of its own.
        Assert.Equal("TS-42-login", Assert.Single(commits, c => c.Sha == "ahead1").ViaBranchName);
        Assert.Null(Assert.Single(commits, c => c.Sha == "direct1").ViaBranchName);
    }

    private TaskSphere.Domain.Entities.GitHubCommit NewCommit(string sha, string message) => new()
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _repositoryId,
        Sha = sha,
        Message = message,
        AuthorName = "Rigon",
        CommittedAtUtc = DateTime.UtcNow,
        HtmlUrl = $"https://github.com/rigon-org/api/commit/{sha}",
    };
}
