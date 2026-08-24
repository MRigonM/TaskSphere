using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubPullRequest = TaskSphere.Domain.Entities.GitHubPullRequest;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class MergeTransitionTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMergeTransitionTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _tsProjectId;      // key "TS", linked to api, AutoDoneOnMerge = true
    private int _bsProjectId;      // key "BS", linked to nothing, AutoDoneOnMerge = true
    private int _optOutProjectId;  // key "OO", linked to api, AutoDoneOnMerge = false
    private int _apiRepositoryId;
    private int _webRepositoryId;  // linked to no project

    private int _ts42TaskId;
    private int _ts51TaskId;
    private int _ts60TaskId;
    private int _bs42TaskId;
    private int _oo9TaskId;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static MergeTransitionService NewService(ApplicationDbContext db, AuditQueue queue)
        => new(new UnitOfWork(db), queue);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Merge Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        // DECOY ROWS — see Global Constraints. Projects, Repositories, Tasks and PullRequests
        // must not share identity values, or a wrong-id lookup passes by accident.
        db.Projects.AddRange(
            new Project { Name = "Decoy A", Key = "DECA", CompanyId = _companyId },
            new Project { Name = "Decoy B", Key = "DECB", CompanyId = _companyId },
            new Project { Name = "Decoy C", Key = "DECC", CompanyId = _companyId },
            new Project { Name = "Decoy D", Key = "DECD", CompanyId = _companyId });
        await db.SaveChangesAsync();

        var ts = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId, AutoDoneOnMerge = true };
        var bs = new Project { Name = "BaseClean", Key = "BS", CompanyId = _companyId, AutoDoneOnMerge = true };
        var oo = new Project { Name = "Opted Out", Key = "OO", CompanyId = _companyId, AutoDoneOnMerge = false };
        db.Projects.AddRange(ts, bs, oo);
        await db.SaveChangesAsync();
        _tsProjectId = ts.Id;
        _bsProjectId = bs.Id;
        _optOutProjectId = oo.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 9701,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var api = new GitHubRepository
        {
            RepositoryId = 9801,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var web = new GitHubRepository
        {
            RepositoryId = 9802,
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
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
        await db.SaveChangesAsync();

        var ts42 = new TaskEntity { Title = "Panel", Number = 42, ProjectId = _tsProjectId, CompanyId = _companyId, Status = TaskStatuses.InProgress };
        var ts51 = new TaskEntity { Title = "Sync", Number = 51, ProjectId = _tsProjectId, CompanyId = _companyId, Status = TaskStatuses.Blocked };
        var ts60 = new TaskEntity { Title = "Tab", Number = 60, ProjectId = _tsProjectId, CompanyId = _companyId, Status = TaskStatuses.Open };
        var bs42 = new TaskEntity { Title = "Purge", Number = 42, ProjectId = _bsProjectId, CompanyId = _companyId, Status = TaskStatuses.Open };
        var oo9 = new TaskEntity { Title = "Ignore", Number = 9, ProjectId = _optOutProjectId, CompanyId = _companyId, Status = TaskStatuses.Open };
        db.Set<TaskEntity>().AddRange(ts42, ts51, ts60, bs42, oo9);
        await db.SaveChangesAsync();

        _ts42TaskId = ts42.Id;
        _ts51TaskId = ts51.Id;
        _ts60TaskId = ts60.Id;
        _bs42TaskId = bs42.Id;
        _oo9TaskId = oo9.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private async SystemTask.Task<int> AddPullRequest(
        int repositoryId,
        int number,
        string headBranch,
        PullRequestState state = PullRequestState.Merged,
        DateTime? markerAppliedAt = null)
    {
        await using var db = NewContext();
        var pull = new GitHubPullRequest
        {
            GitHubRepositoryId = repositoryId,
            CompanyId = _companyId,
            Number = number,
            Title = "A pull request",
            State = state,
            AuthorLogin = "rigon",
            HeadBranch = headBranch,
            OpenedAtUtc = DateTime.UtcNow.AddDays(-1),
            GitHubUpdatedAtUtc = DateTime.UtcNow,
            MergedAtUtc = state == PullRequestState.Merged ? DateTime.UtcNow : null,
            HtmlUrl = $"https://github.com/rigon-org/api/pull/{number}",
            MergeTransitionAppliedAtUtc = markerAppliedAt,
        };
        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();
        return pull.Id;
    }

    private async SystemTask.Task<string> StatusOf(int taskId)
    {
        await using var db = NewContext();
        var task = await db.Set<TaskEntity>().SingleAsync(t => t.Id == taskId);
        return task.Status;
    }

    private async SystemTask.Task<DateTime?> MarkerOf(int pullRequestId)
    {
        await using var db = NewContext();
        var pull = await db.GitHubPullRequests.SingleAsync(p => p.Id == pullRequestId);
        return pull.MergeTransitionAppliedAtUtc;
    }

    [Fact]
    public async SystemTask.Task Moves_an_in_progress_task_to_done_when_its_branch_pull_request_merges()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 1, "TS-42/add-the-panel");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Moves_an_open_task_to_done()
    {
        await AddPullRequest(_apiRepositoryId, 2, "TS-60/the-tab");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(1, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts60TaskId));
    }

    [Fact]
    public async SystemTask.Task Ignores_a_pull_request_that_is_open_rather_than_merged()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 3, "TS-42/add-the-panel", PullRequestState.Open);

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts42TaskId));
        // Not considered at all: an open pull request may still merge later.
        Assert.Null(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Stamps_the_marker_for_a_branch_that_names_no_key()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 4, "hotfix/login");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(1, result.Value!.Skipped);
        // Its branch name cannot change retroactively, so it is never worth reconsidering.
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Leaves_a_blocked_task_alone_but_still_stamps_the_marker()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 10, "TS-51/the-sync");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // Someone deliberately flagged a problem; a merge does not clear it.
        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Blocked, await StatusOf(_ts51TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Does_not_revisit_a_blocked_task_after_it_is_unblocked()
    {
        await AddPullRequest(_apiRepositoryId, 11, "TS-51/the-sync");

        await using (var first = NewContext())
            await NewService(first, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        await using (var edit = NewContext())
        {
            var task = await edit.Set<TaskEntity>().SingleAsync(t => t.Id == _ts51TaskId);
            task.Status = TaskStatuses.InProgress;
            await edit.SaveChangesAsync();
        }

        await using var second = NewContext();
        var result = await NewService(second, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts51TaskId));
    }

    [Fact]
    public async SystemTask.Task Writes_no_status_when_the_project_has_opted_out_but_stamps_the_marker()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 12, "OO-9/ignore-me");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Open, await StatusOf(_oo9TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Enabling_the_toggle_later_does_not_retroactively_move_the_task()
    {
        await AddPullRequest(_apiRepositoryId, 13, "OO-9/ignore-me");

        await using (var first = NewContext())
            await NewService(first, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        await using (var edit = NewContext())
        {
            var project = await edit.Projects.SingleAsync(p => p.Id == _optOutProjectId);
            project.AutoDoneOnMerge = true;
            await edit.SaveChangesAsync();
        }

        await using var second = NewContext();
        var result = await NewService(second, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // Deliberate: ticking a checkbox must not mass-move a month of merged work.
        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Open, await StatusOf(_oo9TaskId));
    }

    [Fact]
    public async SystemTask.Task Leaves_a_task_that_is_already_done_alone()
    {
        await using (var edit = NewContext())
        {
            var task = await edit.Set<TaskEntity>().SingleAsync(t => t.Id == _ts60TaskId);
            task.Status = TaskStatuses.Done;
            await edit.SaveChangesAsync();
        }

        await AddPullRequest(_apiRepositoryId, 14, "TS-60/the-tab");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts60TaskId));
    }
}
