using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The sync service is the only component that parses GitHub JSON. Everything here asserts on
/// captured request URLs and on the mirror afterwards — never on GitHub itself, per B1's
/// strategy of faking the API surface rather than reaching for a real token.
/// </summary>
public class GitHubActivitySyncTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubActivitySyncTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const long TheInstallationId = 7301;

    private Guid _companyId;
    private int _apiRepositoryId;   // linked to TS
    private int _webRepositoryId;   // linked to nothing
    private int _ts42TaskId;

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

        var company = new Company { Name = "Sync Activity Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = TheInstallationId,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var api = new GitHubRepository
        {
            RepositoryId = 8301,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var web = new GitHubRepository
        {
            RepositoryId = 8302,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/web",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.AddRange(api, web);
        await db.SaveChangesAsync();

        _apiRepositoryId = api.Id;
        _webRepositoryId = web.Id;

        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            ProjectId = project.Id,
            GitHubRepositoryId = _apiRepositoryId,
            CompanyId = _companyId,
            LinkedByUserId = "rigon",
        });

        var ts42 = new TaskEntity { Title = "Panel", Number = 42, ProjectId = project.Id, CompanyId = _companyId };
        db.Set<TaskEntity>().Add(ts42);
        await db.SaveChangesAsync();
        _ts42TaskId = ts42.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    // ---- fake API client ---------------------------------------------------------------

    /// <summary>
    /// Keyed by URL substring rather than a queue: the sync makes one commits call per branch,
    /// so the call order is data-dependent and a queue would couple the test to it. Captures
    /// every requested URL, which is how the "only linked repositories are fetched" and
    /// "the since parameter carries the window" assertions are made.
    /// </summary>
    private sealed class FakeApiClient : IGitHubApiClient
    {
        private readonly List<(string Match, string Body)> _responses = new();
        private readonly Dictionary<string, Error> _failures = new(StringComparer.Ordinal);

        public List<string> RequestedUrls { get; } = new();

        public FakeApiClient On(string urlContains, string body)
        {
            _responses.Add((urlContains, body));
            return this;
        }

        public FakeApiClient Fail(string urlContains, Error error)
        {
            _failures[urlContains] = error;
            return this;
        }

        public Task<Result<GitHubResponse>> GetAsync(long installationId, string url, CancellationToken cancellationToken = default)
        {
            RequestedUrls.Add(url);

            foreach (var (match, error) in _failures)
                if (url.Contains(match, StringComparison.Ordinal))
                    return Task.FromResult(Result<GitHubResponse>.Failure(error));

            foreach (var (match, body) in _responses)
                if (url.Contains(match, StringComparison.Ordinal))
                    return Task.FromResult(Result<GitHubResponse>.Success(new GitHubResponse(body, null)));

            return Task.FromResult(Result<GitHubResponse>.Success(new GitHubResponse("[]", null)));
        }
    }

    private static string Branches(params (string Name, string Sha)[] branches)
        => "[" + string.Join(",", branches.Select(b =>
            $"{{\"name\":\"{b.Name}\",\"commit\":{{\"sha\":\"{b.Sha}\"}}}}")) + "]";

    private async SystemTask.Task<Result<SyncActivityResultDto>> Sync(FakeApiClient api)
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        var resolver = new GitHubTaskLinkResolver(uow);

        return await new GitHubActivitySyncService(api, uow, resolver).SyncCompanyAsync(_companyId);
    }

    // ---- tests ---------------------------------------------------------------------------

    [Fact]
    public async SystemTask.Task OnlyRepositoriesWithALiveProjectLink_AreFetched()
    {
        // Not a cost optimisation: an unlinked repository provably cannot produce a link, so
        // fetching it is work that yields nothing by definition.
        var api = new FakeApiClient().On("/repos/rigon-org/api/branches", Branches(("main", "aaa")));

        var result = await Sync(api);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.RepositoriesSynced);

        Assert.Contains(api.RequestedUrls, u => u.Contains("rigon-org/api", StringComparison.Ordinal));
        Assert.DoesNotContain(api.RequestedUrls, u => u.Contains("rigon-org/web", StringComparison.Ordinal));
    }

    [Fact]
    public async SystemTask.Task Branches_AreUpserted_WithNameAndHeadSha()
    {
        var api = new FakeApiClient()
            .On("/repos/rigon-org/api/branches", Branches(("main", "aaa1111"), ("TS-42-fix", "bbb2222")));

        var result = await Sync(api);

        Assert.Equal(2, result.Value!.Branches);

        await using var db = NewContext();
        var branches = await db.GitHubBranches.OrderBy(b => b.Name).ToListAsync();

        Assert.Equal(2, branches.Count);
        Assert.Equal("main", branches[0].Name);
        Assert.Equal("aaa1111", branches[0].HeadSha);
        Assert.Equal("TS-42-fix", branches[1].Name);
        Assert.Equal("bbb2222", branches[1].HeadSha);
        Assert.All(branches, b => Assert.Equal(_apiRepositoryId, b.GitHubRepositoryId));
        Assert.All(branches, b => Assert.Equal(_companyId, b.CompanyId));
    }

    [Fact]
    public async SystemTask.Task ReSyncingTheSameBranches_CreatesNoDuplicates_AndUpdatesTheHead()
    {
        await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa1111"))));
        await Sync(new FakeApiClient().On("/branches", Branches(("main", "ccc3333"))));

        await using var db = NewContext();
        var branch = await db.GitHubBranches.SingleAsync();

        Assert.Equal("ccc3333", branch.HeadSha);
        Assert.Equal(1, await db.GitHubBranches.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async SystemTask.Task ABranchAbsentFromTheResponse_IsSoftDeleted_AndItsTaskLinkSurvives()
    {
        await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb"))));

        await using (var db = NewContext())
            Assert.Single(await db.TaskLinks.ToListAsync());

        await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa"))));

        await using (var db = NewContext())
        {
            Assert.Single(await db.GitHubBranches.ToListAsync());

            var gone = await db.GitHubBranches.IgnoreQueryFilters().SingleAsync(b => b.Name == "TS-42-fix");
            Assert.True(gone.IsDeleted);
            Assert.NotNull(gone.DeletedAt);

            // Same principle as unavailableCount on the links screen: a thing that went away
            // is reported, never silently vanished.
            var link = await db.TaskLinks.SingleAsync();
            Assert.Equal(gone.Id, link.GitHubBranchId);
        }
    }

    [Fact]
    public async SystemTask.Task ABranchDeletedThenRecreated_RevivesTheSameRow_RatherThanColliding()
    {
        // The exact case the unfiltered unique index exists for. A filtered upsert would find
        // nothing, insert, and violate IX_GitHubBranches_RepositoryId_Name.
        await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb"))));
        await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa"))));

        var result = await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa"), ("TS-42-fix", "ddd4444"))));

        Assert.True(result.IsSuccess);

        await using var db = NewContext();
        var revived = await db.GitHubBranches.SingleAsync(b => b.Name == "TS-42-fix");

        Assert.False(revived.IsDeleted);
        Assert.Null(revived.DeletedAt);
        Assert.Equal("ddd4444", revived.HeadSha);
        Assert.Equal(2, await db.GitHubBranches.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async SystemTask.Task TheResolverRuns_SoABranchNamedForATask_LinksWithoutASecondCall()
    {
        await Sync(new FakeApiClient().On("/branches", Branches(("TS-42-fix", "bbb"))));

        await using var db = NewContext();
        var link = await db.TaskLinks.SingleAsync();

        Assert.Equal(_ts42TaskId, link.TaskId);
        Assert.NotNull(link.GitHubBranchId);
    }

    [Fact]
    public async SystemTask.Task ACompanyWithNoInstallation_IsAFailure_NotAnEmptySuccess()
    {
        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            installation.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var result = await Sync(new FakeApiClient());

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.NotConnected", result.Errors[0].Code);
    }
}
