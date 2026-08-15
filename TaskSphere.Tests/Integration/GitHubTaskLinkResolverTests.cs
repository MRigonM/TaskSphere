using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
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
        // Same key, different company: the cross-tenant case.
        var foreign = new Project { Name = "Foreign TS", Key = "TS", CompanyId = _otherCompanyId };
        db.Projects.AddRange(ts, bs, foreign);
        await db.SaveChangesAsync();

        _tsProjectId = ts.Id;
        _bsProjectId = bs.Id;

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

        await Resolve();

        await using var db = NewContext();
        Assert.Empty(await db.TaskLinks.ToListAsync());
        Assert.DoesNotContain(_bs7TaskId, await db.TaskLinks.Select(l => l.TaskId).ToListAsync());
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
}
