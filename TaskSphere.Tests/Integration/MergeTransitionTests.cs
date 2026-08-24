using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
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

    [Fact]
    public async SystemTask.Task Applies_exactly_once_across_repeated_passes()
    {
        await AddPullRequest(_apiRepositoryId, 20, "TS-42/add-the-panel");

        var queue = new AuditQueue();

        await using (var first = NewContext())
        {
            var result = await NewService(first, queue).ApplyAsync(_companyId, "rigon", default);
            Assert.Equal(1, result.Value!.Transitioned);
        }

        await using var second = NewContext();
        var again = await NewService(second, queue).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, again.Value!.Transitioned);
        Assert.Equal(0, again.Value!.Skipped);
    }

    [Fact]
    public async SystemTask.Task Does_not_re_apply_after_a_human_moves_the_task_back()
    {
        await AddPullRequest(_apiRepositoryId, 21, "TS-42/add-the-panel");

        await using (var first = NewContext())
            await NewService(first, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));

        // A lead decides the work is not finished after all.
        await using (var edit = NewContext())
        {
            var task = await edit.Set<TaskEntity>().SingleAsync(t => t.Id == _ts42TaskId);
            task.Status = TaskStatuses.InProgress;
            await edit.SaveChangesAsync();
        }

        await using var second = NewContext();
        var result = await NewService(second, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // The whole reason the marker exists: a status change is an action, not a fact, and
        // re-applying it every sync would overrule the human.
        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts42TaskId));
    }

    [Fact]
    public async SystemTask.Task Refuses_a_key_whose_project_is_not_linked_to_the_pull_requests_repository()
    {
        // BS-42 exists and is Open, and BS has AutoDoneOnMerge = true — the ONLY thing that
        // may stop this is the repository↔project link. api is linked to TS and OO, not BS.
        var pullId = await AddPullRequest(_apiRepositoryId, 22, "BS-42/purge-it");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Open, await StatusOf(_bs42TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Refuses_every_key_from_a_repository_linked_to_no_project()
    {
        var pullId = await AddPullRequest(_webRepositoryId, 23, "TS-42/add-the-panel");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts42TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Transitions_every_task_a_multi_key_branch_names_then_stamps_once()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 24, "TS-42-and-TS-60/two-at-once");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(2, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts60TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task A_pull_request_pointing_at_a_deleted_task_does_not_discard_earlier_work()
    {
        // Two pull requests in one pass. The first moves a task; the second names a task that
        // was soft-deleted after the map was built, so it resolves to nothing.
        await AddPullRequest(_apiRepositoryId, 30, "TS-42/add-the-panel");
        await AddPullRequest(_apiRepositoryId, 31, "TS-60/the-tab");

        await using (var edit = NewContext())
        {
            var task = await edit.Set<TaskEntity>().SingleAsync(t => t.Id == _ts60TaskId);
            task.IsDeleted = true;
            task.DeletedAt = DateTime.UtcNow;
            await edit.SaveChangesAsync();
        }

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // The first pull request's work is persisted regardless of what the second one does.
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.Equal(1, result.Value!.Transitioned);
    }

    [Fact]
    public async SystemTask.Task Reports_success_with_counts_rather_than_an_error_when_nothing_is_pending()
    {
        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new MergeTransitionResult(0, 0, 0), result.Value);
    }

    [Fact]
    public async SystemTask.Task A_pull_request_whose_write_fails_does_not_discard_earlier_work()
    {
        // The soft-deleted-task version of this test does not actually witness the
        // per-pull-request save: nothing throws, so moving SaveChangesAsync out of the loop
        // keeps it green. This one makes one pull request's write genuinely fail, which is
        // the only thing that can tell the two persistence units apart.
        await AddPullRequest(_apiRepositoryId, 32, "TS-42/add-the-panel");
        await AddPullRequest(_apiRepositoryId, 33, "TS-60/the-tab");

        await using (var edit = NewContext())
            await edit.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Tasks ADD CONSTRAINT CK_MergeTransitionTest " +
                $"CHECK (NOT ([Id] = {_ts60TaskId} AND [Status] = 'Done'))");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // A failure is a count, not an abort — and the pull request that already succeeded
        // keeps its work.
        Assert.Equal(1, result.Value!.Transitioned);
        Assert.Equal(1, result.Value!.Failed);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.Equal(TaskStatuses.Open, await StatusOf(_ts60TaskId));
    }

    [Fact]
    public async SystemTask.Task A_failed_pull_request_does_not_poison_the_ones_after_it()
    {
        // The failed write leaves its change tracked in the same DbContext. If it is not
        // discarded, the NEXT pull request's save re-attempts it and fails too, turning one
        // bad row into the rest of the pass.
        await AddPullRequest(_apiRepositoryId, 34, "TS-60/the-tab");
        await AddPullRequest(_apiRepositoryId, 35, "TS-42/add-the-panel");

        await using (var edit = NewContext())
            await edit.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Tasks ADD CONSTRAINT CK_MergeTransitionTest " +
                $"CHECK (NOT ([Id] = {_ts60TaskId} AND [Status] = 'Done'))");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(1, result.Value!.Failed);
        Assert.Equal(1, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
    }
}
