using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubCommit = TaskSphere.Domain.Entities.GitHubCommit;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The resolver never sees GitHub JSON — it reads the mirror. Its one non-negotiable rule is
/// that a key is honoured only when the record's repository is linked to that key's project:
/// keys route, the repo link authorizes.
/// </summary>
public class GitHubTaskLinkResolverTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubResolverTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private Guid _otherCompanyId;

    private int _tsProjectId;      // key "TS", linked to _apiRepositoryId
    private int _bsProjectId;      // key "BS", linked to nothing
    private int _tsxProjectId;     // key "TSX", linked to nothing — "TS" is a prefix of it
    private int _apiRepositoryId;
    private int _webRepositoryId;  // linked to no project at all

    private int _ts42TaskId;
    private int _ts51TaskId;
    private int _bs7TaskId;
    private int _bs42TaskId;       // same Number as TS-42, in the other project

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

        var company = new Company { Name = "Resolver Co" };
        var other = new Company { Name = "Other Co" };
        db.Companies.AddRange(company, other);
        await db.SaveChangesAsync();

        _companyId = company.Id;
        _otherCompanyId = other.Id;

        var ts = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        var bs = new Project { Name = "BaseClean", Key = "BS", CompanyId = _companyId };
        // "TS" is a prefix of "TSX", and both are ordinary keys. Key matching must be exact,
        // not prefix-wise, or TSX-42 routes into TS.
        var tsx = new Project { Name = "TaskSphere X", Key = "TSX", CompanyId = _companyId };
        // Same key, different company: the cross-tenant case.
        var foreign = new Project { Name = "Foreign TS", Key = "TS", CompanyId = _otherCompanyId };
        db.Projects.AddRange(ts, bs, tsx, foreign);
        await db.SaveChangesAsync();

        _tsProjectId = ts.Id;
        _bsProjectId = bs.Id;
        _tsxProjectId = tsx.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 7201,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var api = new GitHubRepository
        {
            RepositoryId = 8201,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var web = new GitHubRepository
        {
            RepositoryId = 8202,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/web",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.AddRange(api, web);
        await db.SaveChangesAsync();

        _apiRepositoryId = api.Id;
        _webRepositoryId = web.Id;

        // Only one link in the whole fixture: api → TS. Everything else is unauthorized.
        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            ProjectId = _tsProjectId,
            GitHubRepositoryId = _apiRepositoryId,
            CompanyId = _companyId,
            LinkedByUserId = "rigon",
        });

        var ts42 = new TaskEntity { Title = "Panel", Number = 42, ProjectId = _tsProjectId, CompanyId = _companyId };
        var ts51 = new TaskEntity { Title = "Sync", Number = 51, ProjectId = _tsProjectId, CompanyId = _companyId };
        var bs7 = new TaskEntity { Title = "Audit", Number = 7, ProjectId = _bsProjectId, CompanyId = _companyId };
        // Deliberately the same Number as TS-42: routing a key by its number alone, or against
        // the wrong project, would land here.
        var bs42 = new TaskEntity { Title = "Purge", Number = 42, ProjectId = _bsProjectId, CompanyId = _companyId };
        db.Set<TaskEntity>().AddRange(ts42, ts51, bs7, bs42);
        await db.SaveChangesAsync();

        _ts42TaskId = ts42.Id;
        _ts51TaskId = ts51.Id;
        _bs7TaskId = bs7.Id;
        _bs42TaskId = bs42.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private async SystemTask.Task<int> SeedCommit(string message, int repositoryId, Guid? companyId = null)
    {
        await using var db = NewContext();

        var commit = new GitHubCommit
        {
            GitHubRepositoryId = repositoryId,
            CompanyId = companyId ?? _companyId,
            Sha = Guid.NewGuid().ToString("N") + "12345678",
            Message = message,
            AuthorName = "Rigon",
            CommittedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/commit/x",
        };

        db.GitHubCommits.Add(commit);
        await db.SaveChangesAsync();

        return commit.Id;
    }

    private async SystemTask.Task<int> SeedBranch(string name, int repositoryId, Guid? companyId = null)
    {
        await using var db = NewContext();

        var branch = new TaskSphere.Domain.Entities.GitHubBranch
        {
            GitHubRepositoryId = repositoryId,
            CompanyId = companyId ?? _companyId,
            Name = name,
            HeadSha = "aaaaaaa",
        };

        db.GitHubBranches.Add(branch);
        await db.SaveChangesAsync();

        return branch.Id;
    }

    private async SystemTask.Task<int> SeedPullRequest(int number, string title, string? body, int repositoryId, Guid? companyId = null, string? headBranch = null)
    {
        await using var db = NewContext();

        var pull = new TaskSphere.Domain.Entities.GitHubPullRequest
        {
            GitHubRepositoryId = repositoryId,
            CompanyId = companyId ?? _companyId,
            Number = number,
            Title = title,
            Body = body,
            State = PullRequestState.Open,
            AuthorLogin = "MRigonM",
            HeadBranch = headBranch ?? "main",
            OpenedAtUtc = DateTime.UtcNow,
            GitHubUpdatedAtUtc = DateTime.UtcNow,
            HtmlUrl = $"https://github.com/rigon-org/api/pull/{number}",
        };

        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();

        return pull.Id;
    }

    private async SystemTask.Task<TaskLinkResolution> Resolve()
    {
        await using var db = NewContext();
        var resolver = new GitHubTaskLinkResolver(new UnitOfWork(db));
        return await resolver.ResolveAsync(_companyId);
    }

    [Fact]
    public async SystemTask.Task AKeyWhoseRepositoryIsNotLinkedToThatProject_ProducesNoLink()
    {
        // THE security test. Do not delete it, do not weaken it. Without the link check,
        // anyone with push access to any repository under the installation can attach
        // activity to a project they are not a member of.
        await SeedCommit("TS-42 sneak in", _webRepositoryId);

        var result = await Resolve();

        Assert.Equal(0, result.LinksCreated);

        await using var db = NewContext();
        Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task AKeyWhoseRepositoryIsLinked_ProducesALink()
    {
        var commitId = await SeedCommit("TS-42 wire the panel", _apiRepositoryId);

        var result = await Resolve();

        Assert.Equal(1, result.LinksCreated);

        await using var db = NewContext();
        var link = await db.TaskLinks.SingleAsync();

        Assert.Equal(_ts42TaskId, link.TaskId);
        Assert.Equal(commitId, link.GitHubCommitId);
        Assert.Null(link.GitHubBranchId);
        Assert.Null(link.GitHubPullRequestId);
        Assert.Equal(_companyId, link.CompanyId);
    }

    [Fact]
    public async SystemTask.Task OneCommitNamingTwoKeys_CreatesTwoLinks()
    {
        await SeedCommit("TS-42 TS-51 fix both", _apiRepositoryId);

        var result = await Resolve();

        Assert.Equal(2, result.LinksCreated);

        await using var db = NewContext();
        var taskIds = await db.TaskLinks.Select(l => l.TaskId).OrderBy(id => id).ToListAsync();

        Assert.Equal([_ts42TaskId, _ts51TaskId], taskIds);
    }

    [Fact]
    public async SystemTask.Task ARepositoryLinkedToTwoProjects_RoutesEachKeyToItsOwnProjectsTask()
    {
        // One repository feeding two projects is a first-class case, not an edge one. Both
        // projects hold a task numbered 42, so a resolver that routed by number alone — or
        // authorized against one project and then looked the task up in another — would
        // attach BS's activity to TS-42.
        await using (var db = NewContext())
        {
            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                ProjectId = _bsProjectId,
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
            await db.SaveChangesAsync();
        }

        var commitId = await SeedCommit("TS-42 and BS-42 in one push", _apiRepositoryId);

        Assert.Equal(2, (await Resolve()).LinksCreated);

        await using (var db = NewContext())
        {
            var links = await db.TaskLinks.ToListAsync();

            Assert.All(links, l => Assert.Equal(commitId, l.GitHubCommitId));
            Assert.Equal(
                new[] { _ts42TaskId, _bs42TaskId }.OrderBy(id => id).ToList(),
                links.Select(l => l.TaskId).OrderBy(id => id).ToList());
        }
    }

    [Fact]
    public async SystemTask.Task AKeyForAProjectWithNoLinkedRepositoryAtAll_ProducesNoLink()
    {
        // BS exists and BS-7 exists; what is missing is a link from api to BS.
        await SeedCommit("BS-7 from the api repo", _apiRepositoryId);

        Assert.Equal(0, (await Resolve()).LinksCreated);

        await using var db = NewContext();
        Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task AKeyNamingAnotherCompanysProject_ProducesNoLink()
    {
        // The other company also has a project keyed TS. Resolution is scoped by company, so
        // this commit resolves against nothing at all.
        await SeedCommit("TS-42 belongs elsewhere", _apiRepositoryId, _otherCompanyId);

        var result = await Resolve();

        Assert.Equal(0, result.LinksCreated);

        await using var db = NewContext();
        Assert.Empty(await db.TaskLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async SystemTask.Task UnparseableAndUnknownKeys_AreSilentlyIgnored()
    {
        await SeedCommit("no key here at all", _apiRepositoryId);
        await SeedCommit("ZZ-99 unknown project", _apiRepositoryId);
        await SeedCommit("TS-999 unknown task", _apiRepositoryId);

        var result = await Resolve();

        Assert.Equal(0, result.LinksCreated);
        Assert.Equal(2, result.KeysSeen);        // ZZ-99 and TS-999; the first commit names none
        Assert.Equal(2, result.KeysUnresolved);

        await using var db = NewContext();
        Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ReRunningTheResolver_CreatesNoDuplicateLinks()
    {
        await SeedCommit("TS-42 wire the panel", _apiRepositoryId);

        var first = await Resolve();
        var second = await Resolve();

        Assert.Equal(1, first.LinksCreated);
        Assert.Equal(0, second.LinksCreated);

        await using var db = NewContext();
        Assert.Single(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ALinkCreatedAfterTheCommitWasIngested_IsPickedUpOnTheNextRun()
    {
        // Why the resolver scans the whole live mirror rather than the rows a sync just
        // touched: a touched-set pass would miss this permanently.
        await SeedCommit("BS-7 audit logging", _apiRepositoryId);

        Assert.Equal(0, (await Resolve()).LinksCreated);

        await using (var db = NewContext())
        {
            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                ProjectId = _bsProjectId,
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, (await Resolve()).LinksCreated);

        await using (var db = NewContext())
        {
            var link = await db.TaskLinks.SingleAsync();
            Assert.Equal(_bs7TaskId, link.TaskId);
        }
    }

    [Fact]
    public async SystemTask.Task ASoftDeletedTask_IsNeverLinked()
    {
        // Reachable through the ordinary task API. It matters permanently, not just until the
        // next sync: the unique index on Task(ProjectId, Number) is filtered on
        // [ProjectId] IS NOT NULL only — not on IsDeleted — so a deleted TS-51 reserves
        // number 51 forever, and every later commit naming TS-51 must resolve to nothing.
        await using (var db = NewContext())
        {
            var task = await db.Set<TaskEntity>().SingleAsync(t => t.Id == _ts51TaskId);
            task.IsDeleted = true;
            task.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await SeedCommit("TS-51 resurrect the dead", _apiRepositoryId);

        Assert.Equal(0, (await Resolve()).LinksCreated);

        await using (var db = NewContext())
            Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task AKeyWhoseProjectKeyMerelyStartsWithAnother_DoesNotRouteIntoIt()
    {
        // TSX is its own project and is linked to nothing. Matching project keys by prefix
        // rather than exactly would route TSX-42 into TS, which is linked to api — a
        // cross-project breach dressed up as a lookup.
        await SeedCommit("TSX-42 unrelated", _apiRepositoryId);

        Assert.Equal(0, (await Resolve()).LinksCreated);

        await using (var db = NewContext())
        {
            var links = await db.TaskLinks.ToListAsync();
            Assert.Empty(links);
            Assert.DoesNotContain(_ts42TaskId, links.Select(l => l.TaskId));
        }
    }

    [Fact]
    public async SystemTask.Task TwoCommitsNamingTheSameKey_EachGetTheirOwnLink()
    {
        // Suppression is per (task, commit), not per task. Keyed on the task alone, a task
        // named by five commits would hold one link and its panel would show one commit —
        // silent data loss that a single-commit re-run test cannot see.
        var first = await SeedCommit("TS-42 first half", _apiRepositoryId);
        var second = await SeedCommit("TS-42 second half", _apiRepositoryId);

        var result = await Resolve();

        Assert.Equal(2, result.LinksCreated);
        Assert.Equal(2, result.KeysSeen);

        // And the pair survives a re-run: still two rows, still no duplicate.
        Assert.Equal(0, (await Resolve()).LinksCreated);

        await using (var db = NewContext())
        {
            var links = await db.TaskLinks.ToListAsync();

            Assert.Equal(2, links.Count);
            Assert.All(links, l => Assert.Equal(_ts42TaskId, l.TaskId));
            Assert.Equal(
                new[] { first, second }.OrderBy(id => id).ToList(),
                links.Select(l => l.GitHubCommitId!.Value).OrderBy(id => id).ToList());
        }
    }

    [Fact]
    public async SystemTask.Task ALinkRowBelongingToAnotherCompany_DoesNotAuthorize()
    {
        // Defence in depth. No writer produces this row today — GitHubProjectLinkService
        // validates both ends — but the authorized set is only as tenant-safe as its own
        // predicate, and B2's webhook-driven auto-link is exactly the kind of future writer
        // that might not validate.
        //
        // BS rather than TS: the unique index on (ProjectId, GitHubRepositoryId) is filtered
        // on [IsDeleted] = 0 and does not include CompanyId, so a second live row over
        // (TS, api) would be rejected by the index before the resolver ever saw it.
        await using (var db = NewContext())
        {
            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                ProjectId = _bsProjectId,
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _otherCompanyId,
                LinkedByUserId = "intruder",
            });
            await db.SaveChangesAsync();
        }

        await SeedCommit("BS-7 authorized by someone else's link", _apiRepositoryId);

        Assert.Equal(0, (await Resolve()).LinksCreated);

        await using (var db = NewContext())
            Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ASoftDeletedLink_DoesNotAuthorize()
    {
        await using (var db = NewContext())
        {
            var link = await db.ProjectRepositoryLinks.SingleAsync();
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await SeedCommit("TS-42 after unlink", _apiRepositoryId);

        Assert.Equal(0, (await Resolve()).LinksCreated);

        await using (var db = NewContext())
            Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ABranchNameCarryingAKey_ProducesALink()
    {
        var branchId = await SeedBranch("feature/TS-42-activity-panel", _apiRepositoryId);

        var result = await Resolve();

        Assert.Equal(1, result.LinksCreated);

        await using var db = NewContext();
        var link = await db.TaskLinks.SingleAsync();

        Assert.Equal(_ts42TaskId, link.TaskId);
        Assert.Equal(branchId, link.GitHubBranchId);
        Assert.Null(link.GitHubCommitId);
        Assert.Null(link.GitHubPullRequestId);
    }

    [Fact]
    public async SystemTask.Task APullRequestTitle_ProducesALink()
    {
        var pullId = await SeedPullRequest(17, "TS-42 wire the panel", null, _apiRepositoryId);

        await Resolve();

        await using var db = NewContext();
        var link = await db.TaskLinks.SingleAsync();

        Assert.Equal(_ts42TaskId, link.TaskId);
        Assert.Equal(pullId, link.GitHubPullRequestId);
    }

    [Fact]
    public async SystemTask.Task APullRequestBody_ProducesALink_WhenTheTitleCarriesNoKey()
    {
        await SeedPullRequest(18, "Wire the panel", "Closes TS-51.", _apiRepositoryId);

        await Resolve();

        await using var db = NewContext();
        var link = await db.TaskLinks.SingleAsync();

        Assert.Equal(_ts51TaskId, link.TaskId);
    }

    [Fact]
    public async SystemTask.Task APullRequestNamingTheSameKeyInTitleAndBody_ProducesOneLink()
    {
        await SeedPullRequest(19, "TS-42 wire the panel", "Part of TS-42.", _apiRepositoryId);

        var result = await Resolve();

        Assert.Equal(1, result.LinksCreated);
        // One key, not two: title and body are scanned as one text. LinksCreated alone cannot
        // tell the two designs apart — the duplicate-suppression set would collapse a second
        // attempt anyway — so KeysSeen is what pins the scan down.
        Assert.Equal(1, result.KeysSeen);

        await using var db = NewContext();
        Assert.Single(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ABranchOnAnUnlinkedRepository_ProducesNoLink()
    {
        // The authorization boundary applies to every record kind, not just commits.
        await SeedBranch("TS-42-sneak", _webRepositoryId);
        await SeedPullRequest(20, "TS-42 sneak", "TS-51 too", _webRepositoryId);

        var result = await Resolve();

        Assert.Equal(0, result.LinksCreated);

        await using var db = NewContext();
        Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task OneTaskLinkedFromACommitABranchAndAPullRequest_GetsThreeLinks()
    {
        await SeedCommit("TS-42 wire the panel", _apiRepositoryId);
        await SeedBranch("TS-42-fix", _apiRepositoryId);
        await SeedPullRequest(21, "TS-42 wire the panel", null, _apiRepositoryId);

        var result = await Resolve();

        Assert.Equal(3, result.LinksCreated);

        await using var db = NewContext();
        var links = await db.TaskLinks.ToListAsync();

        Assert.All(links, l => Assert.Equal(_ts42TaskId, l.TaskId));
        Assert.Single(links, l => l.GitHubCommitId is not null);
        Assert.Single(links, l => l.GitHubBranchId is not null);
        Assert.Single(links, l => l.GitHubPullRequestId is not null);
    }

    [Fact]
    public async SystemTask.Task ASoftDeletedBranchOrPullRequest_IsNeverScanned()
    {
        // The branch and pull request reads must carry the soft-delete filter. A stray
        // IgnoreQueryFilters on either one leaks records the mirror has already retired, and
        // no LinksCreated assertion can see it once a link for that record already exists —
        // KeysSeen is the only counter that reacts to the read itself.
        var branchId = await SeedBranch("TS-42-fix", _apiRepositoryId);
        var pullId = await SeedPullRequest(22, "TS-51 sync it", null, _apiRepositoryId);

        await using (var db = NewContext())
        {
            var branch = await db.GitHubBranches.SingleAsync(b => b.Id == branchId);
            branch.IsDeleted = true;
            branch.DeletedAt = DateTime.UtcNow;

            var pull = await db.GitHubPullRequests.SingleAsync(p => p.Id == pullId);
            pull.IsDeleted = true;
            pull.DeletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        var result = await Resolve();

        Assert.Equal(0, result.KeysSeen);
        Assert.Equal(0, result.LinksCreated);

        await using (var db = NewContext())
            Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ASoftDeletedBranch_StopsProducingNewLinks_ButItsExistingLinkSurvives()
    {
        var branchId = await SeedBranch("TS-42-fix", _apiRepositoryId);
        await Resolve();

        await using (var db = NewContext())
        {
            var branch = await db.GitHubBranches.SingleAsync(b => b.Id == branchId);
            branch.IsDeleted = true;
            branch.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var result = await Resolve();

        Assert.Equal(0, result.LinksCreated);

        await using (var db = NewContext())
        {
            // The link is not cleaned up: a branch that went away is reported, never
            // silently vanished.
            var link = await db.TaskLinks.SingleAsync();
            Assert.Equal(branchId, link.GitHubBranchId);
        }
    }

    [Fact]
    public async SystemTask.Task ASoftDeletedCommit_IsNeverScanned()
    {
        // The third kind, pinned for the same reason the branch and pull request above are:
        // a stray IgnoreQueryFilters on the read is the one mutation no downstream assertion
        // catches. Nothing soft-deletes a commit today, which is exactly why this read would
        // rot unwatched — it is the filter that is under test here, not a live scenario.
        var commitId = await SeedCommit("TS-42 wire the panel", _apiRepositoryId);

        await using (var db = NewContext())
        {
            var commit = await db.GitHubCommits.SingleAsync(c => c.Id == commitId);
            commit.IsDeleted = true;
            commit.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var result = await Resolve();

        Assert.Equal(0, result.KeysSeen);
        Assert.Equal(0, result.LinksCreated);

        await using (var db = NewContext())
            Assert.Empty(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ABranchOrPullRequestBelongingToAnotherCompany_IsNeverScanned()
    {
        // The commit read has been pinned to its company since Task 4; these two had not been.
        // Both rows sit on api, which IS linked to TS, so the repository-link check waves them
        // through — the company predicate on the read is the only thing standing between the
        // other tenant's records and a TS task. KeysSeen, not LinksCreated, is what reacts to
        // the read itself.
        await SeedBranch("TS-42-from-elsewhere", _apiRepositoryId, _otherCompanyId);
        await SeedPullRequest(23, "TS-51 from elsewhere", null, _apiRepositoryId, _otherCompanyId);

        var result = await Resolve();

        Assert.Equal(0, result.KeysSeen);
        Assert.Equal(0, result.LinksCreated);

        await using (var db = NewContext())
            Assert.Empty(await db.TaskLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ReRunningTheResolver_CreatesNoDuplicateBranchOrPullRequestLink()
    {
        // Task 4's re-run test only ever seeded a commit, so the branch and pull request halves
        // of the existing-link set were unread. Forget either one and the second run does not
        // merely double-count: it throws, because the per-kind unique indexes are real.
        await SeedBranch("TS-42-fix", _apiRepositoryId);
        await SeedPullRequest(24, "TS-51 sync it", null, _apiRepositoryId);

        var first = await Resolve();
        var second = await Resolve();

        Assert.Equal(2, first.LinksCreated);
        Assert.Equal(0, second.LinksCreated);

        await using (var db = NewContext())
            Assert.Equal(2, await db.TaskLinks.CountAsync());
    }

    [Fact]
    public async SystemTask.Task APullRequestHeadBranch_ProducesALink_WhenTheTitleAndBodyCarryNoKey()
    {
        // The merge → Done transition decides by head branch, so a pull request that can move
        // a task by its branch must also be able to link by it, or the task's history omits the
        // very pull request that closed it.
        var pullId = await SeedPullRequest(25, "Add the panel", null, _apiRepositoryId, headBranch: "TS-42/implement");

        await Resolve();

        await using var db = NewContext();
        var link = await db.TaskLinks.SingleAsync();

        Assert.Equal(_ts42TaskId, link.TaskId);
        Assert.Equal(pullId, link.GitHubPullRequestId);
    }

    [Fact]
    public async SystemTask.Task APullRequestTitle_StillProducesALink()
    {
        // Regression: a pull request whose title names a key must continue to link, even after
        // the resolver begins scanning the head branch as well.
        var pullId = await SeedPullRequest(26, "TS-51 wire the sync", null, _apiRepositoryId, headBranch: "some-other-branch");

        await Resolve();

        await using var db = NewContext();
        var link = await db.TaskLinks.SingleAsync();

        Assert.Equal(_ts51TaskId, link.TaskId);
        Assert.Equal(pullId, link.GitHubPullRequestId);
    }

    [Fact]
    public async SystemTask.Task ACommitAheadOnALinkedBranch_IsInheritedByThatBranchesTask()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        // The branch names TS-42; the commit names nothing at all. Message-only resolution links
        // the branch and stops, which is the gap this feature closes.
        var branch = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
        var commit = new GitHubCommit
        {
            CompanyId = _companyId,
            GitHubRepositoryId = _apiRepositoryId,
            Sha = "ahead1",
            Message = "wire up the login form",
            AuthorName = "Rigon",
            CommittedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/commit/ahead1",
        };
        db.GitHubBranches.Add(branch);
        db.GitHubCommits.Add(commit);
        await db.SaveChangesAsync();

        db.GitHubBranchCommits.Add(new TaskSphere.Domain.Entities.GitHubBranchCommit
        {
            CompanyId = _companyId,
            GitHubBranchId = branch.Id,
            GitHubCommitId = commit.Id,
        });
        await db.SaveChangesAsync();

        var result = await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

        // The branch link AND the inherited commit link, in one run: the branch link is created by
        // the branch pass of this same run and is still unsaved when inheritance reads it.
        Assert.Equal(2, result.LinksCreated);

        var inherited = Assert.Single(await db.TaskLinks.Where(l => l.GitHubCommitId != null).ToListAsync());
        Assert.Equal(_ts42TaskId, inherited.TaskId);
        Assert.Equal(commit.Id, inherited.GitHubCommitId);
        Assert.Equal(branch.Id, inherited.ViaGitHubBranchId);
    }

    [Fact]
    public async SystemTask.Task InheritedLinks_DoNotInflateKeysSeen()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        var branch = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
        var commit = new GitHubCommit
        {
            CompanyId = _companyId,
            GitHubRepositoryId = _apiRepositoryId,
            Sha = "ahead1",
            Message = "wire up the login form",
            AuthorName = "Rigon",
            CommittedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/commit/ahead1",
        };
        db.GitHubBranches.Add(branch);
        db.GitHubCommits.Add(commit);
        await db.SaveChangesAsync();

        db.GitHubBranchCommits.Add(new TaskSphere.Domain.Entities.GitHubBranchCommit
        {
            CompanyId = _companyId,
            GitHubBranchId = branch.Id,
            GitHubCommitId = commit.Id,
        });
        await db.SaveChangesAsync();

        var result = await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

        Assert.Equal(2, result.LinksCreated);   // inheritance ran
        // One key was read in this run — the branch name. Inheritance reads no text, so counting
        // it as a key seen would make the sync summary lie about how much GitHub data was scanned.
        Assert.Equal(1, result.KeysSeen);       // and read no keys doing it
    }

    [Fact]
    public async SystemTask.Task ACommitThatNamesTheTaskAndSitsOnItsBranch_IsOneRowMarkedDirect()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        var branch = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
        var commit = new GitHubCommit
        {
            CompanyId = _companyId,
            GitHubRepositoryId = _apiRepositoryId,
            Sha = "both1",
            Message = "TS-42 wire up the login form",   // names the task ITSELF
            AuthorName = "Rigon",
            CommittedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/commit/both1",
        };
        db.GitHubBranches.Add(branch);
        db.GitHubCommits.Add(commit);
        await db.SaveChangesAsync();

        db.GitHubBranchCommits.Add(new TaskSphere.Domain.Entities.GitHubBranchCommit
        {
            CompanyId = _companyId,
            GitHubBranchId = branch.Id,
            GitHubCommitId = commit.Id,
        });
        await db.SaveChangesAsync();

        await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

        // ONE row — IX_TaskLinks_TaskId_CommitId is unique — and it must read as direct. If the
        // passes ever reorder, this flips to the branch id and the panel starts claiming a commit
        // was inherited when its own message named the task.
        var link = Assert.Single(await db.TaskLinks.Where(l => l.GitHubCommitId == commit.Id).ToListAsync());
        Assert.Null(link.ViaGitHubBranchId);
    }

    [Fact]
    public async SystemTask.Task ACommitAheadOnTwoLinkedBranches_ReachesBothTasks()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        // The case the join table exists for: TS-51's branch was cut from TS-42's, so one commit
        // is ahead of default on both. A column on GitHubCommit could record only one of them.
        var branch42 = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
        var branch51 = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-51-signup", HeadSha = "ccc" };
        var commit = new GitHubCommit
        {
            CompanyId = _companyId,
            GitHubRepositoryId = _apiRepositoryId,
            Sha = "shared-ahead",
            Message = "extract the auth form base",
            AuthorName = "Rigon",
            CommittedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/commit/shared-ahead",
        };
        db.GitHubBranches.AddRange(branch42, branch51);
        db.GitHubCommits.Add(commit);
        await db.SaveChangesAsync();

        db.GitHubBranchCommits.AddRange(
            new TaskSphere.Domain.Entities.GitHubBranchCommit { CompanyId = _companyId, GitHubBranchId = branch42.Id, GitHubCommitId = commit.Id },
            new TaskSphere.Domain.Entities.GitHubBranchCommit { CompanyId = _companyId, GitHubBranchId = branch51.Id, GitHubCommitId = commit.Id });
        await db.SaveChangesAsync();

        await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

        var links = await db.TaskLinks
            .Where(l => l.GitHubCommitId == commit.Id)
            .OrderBy(l => l.TaskId)
            .ToListAsync();

        // Assert the PAIRING, not the count: two rows with the right task ids and the right via
        // branches. Counting two would pass if both rows belonged to the same task.
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.TaskId == _ts42TaskId && l.ViaGitHubBranchId == branch42.Id);
        Assert.Contains(links, l => l.TaskId == _ts51TaskId && l.ViaGitHubBranchId == branch51.Id);
    }

    [Fact]
    public async SystemTask.Task RunningTheResolverTwice_CreatesNoSecondInheritedLink()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        var branch = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
        var commit = new GitHubCommit
        {
            CompanyId = _companyId,
            GitHubRepositoryId = _apiRepositoryId,
            Sha = "ahead1",
            Message = "wire up the login form",
            AuthorName = "Rigon",
            CommittedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/commit/ahead1",
        };
        db.GitHubBranches.Add(branch);
        db.GitHubCommits.Add(commit);
        await db.SaveChangesAsync();

        db.GitHubBranchCommits.Add(new TaskSphere.Domain.Entities.GitHubBranchCommit
        {
            CompanyId = _companyId,
            GitHubBranchId = branch.Id,
            GitHubCommitId = commit.Id,
        });
        await db.SaveChangesAsync();

        await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);
        var second = await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

        // A re-run must insert nothing: the inherited link is seeded back out of the database on
        // run two and recomputes to the same tuple. This is an idempotency test, not the tuple-width
        // guard — a widened `existing` seeds ViaGitHubBranchId back too, so this scenario still
        // matches itself run-over-run. ACommitThatNamesTheTaskAndSitsOnItsBranch_IsOneRowMarkedDirect
        // is what fails if the tuple is widened, and it fails inside a single resolve.
        Assert.Equal(0, second.LinksCreated);
        Assert.Single(await db.TaskLinks.Where(l => l.GitHubCommitId == commit.Id).ToListAsync());
    }
}
