using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

// The entities, not the namespace — TaskSphere.Domain.Entities.Task shadows Task otherwise.
using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Task 15 — repository sync. Matching is on GitHub's numeric id, never on FullName, and every
/// lookup ignores query filters because IX_GitHubRepositories_RepositoryId is unfiltered (§0m).
/// </summary>
public class GitHubRepositorySyncTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubSyncTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const long TheInstallationId = 9500;

    private Guid _companyId;
    private Guid _otherCompanyId;
    private int _installationRowId;

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

        var company = new Company { Name = "Sync Test Co" };
        var other = new Company { Name = "Other Co" };
        db.Companies.AddRange(company, other);
        await db.SaveChangesAsync();

        _companyId = company.Id;
        _otherCompanyId = other.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = TheInstallationId,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.Selected,
        };

        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();
        _installationRowId = installation.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    // ---- fake API client ---------------------------------------------------------------

    private sealed class FakeApiClient : IGitHubApiClient
    {
        private readonly Queue<GitHubResponse> _responses = new();

        public List<string> RequestedUrls { get; } = new();
        public Error? Failure { get; set; }

        public FakeApiClient Enqueue(string body, string? linkHeader = null)
        {
            _responses.Enqueue(new GitHubResponse(body, linkHeader));
            return this;
        }

        public Task<Result<GitHubResponse>> GetAsync(long installationId, string url, CancellationToken cancellationToken = default)
        {
            RequestedUrls.Add(url);

            if (Failure is not null)
                return Task.FromResult(Result<GitHubResponse>.Failure(Failure));

            return Task.FromResult(Result<GitHubResponse>.Success(_responses.Dequeue()));
        }

        /// <summary>
        /// The repository sync is read-only, so a POST from it is a defect rather than a case to
        /// stub. Throwing makes that loud; dequeuing a response would let it pass — and would
        /// consume the response the next GET was waiting for.
        /// </summary>
        public Task<Result<GitHubResponse>> PostAsync(long installationId, string url, string jsonBody, CancellationToken cancellationToken = default)
            => throw new NotSupportedException($"The repository sync must never POST. It attempted {url}.");
    }

    private static string Page(string selection, params (long Id, string FullName, string Branch, bool Private)[] repos)
    {
        var items = string.Join(",", repos.Select(r =>
            $$"""{"id":{{r.Id}},"full_name":"{{r.FullName}}","default_branch":"{{r.Branch}}","private":{{(r.Private ? "true" : "false")}}}"""));

        return $$"""{"total_count":{{repos.Length}},"repository_selection":"{{selection}}","repositories":[{{items}}]}""";
    }

    private async SystemTask.Task<Result<int>> Sync(FakeApiClient api)
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        var installation = await db.GitHubInstallations.IgnoreQueryFilters()
            .SingleAsync(i => i.InstallationId == TheInstallationId);

        return await new GitHubRepositorySyncService(api, uow).SyncAsync(installation);
    }

    // ---- tests ---------------------------------------------------------------------------

    [Fact]
    public async SystemTask.Task Sync_InsertsTheInstallationsRepositories()
    {
        var api = new FakeApiClient()
            .Enqueue(Page("all",
                (1001, "rigon-org/alpha", "main", true),
                (1002, "rigon-org/beta", "develop", false)));

        var result = await Sync(api);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);

        await using var db = NewContext();
        var repos = await db.GitHubRepositories.OrderBy(r => r.RepositoryId).ToListAsync();

        Assert.Equal(2, repos.Count);
        Assert.Equal("rigon-org/alpha", repos[0].FullName);
        Assert.True(repos[0].IsPrivate);
        Assert.Equal("develop", repos[1].DefaultBranch);
        Assert.All(repos, r => Assert.Equal(_installationRowId, r.GitHubInstallationId));
        Assert.All(repos, r => Assert.Equal(_companyId, r.CompanyId));
    }

    [Fact]
    public async SystemTask.Task RepositorySelection_RoundTripsIntoTheInstallation()
    {
        await Sync(new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha", "main", false))));

        await using var db = NewContext();
        var installation = await db.GitHubInstallations.SingleAsync();

        Assert.Equal(RepositorySelection.All, installation.RepositorySelection);
    }

    [Fact]
    public async SystemTask.Task UnrecognisedRepositorySelection_IsAFailure_NotASilentDefault()
    {
        var api = new FakeApiClient().Enqueue(Page("everything", (1001, "rigon-org/alpha", "main", false)));

        var result = await Sync(api);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.UnknownRepositorySelection", result.Errors[0].Code);
    }

    [Fact]
    public async SystemTask.Task RenamedRepository_UpdatesInPlace_RatherThanCreatingARow()
    {
        // The reason matching is on the numeric id: a rename would otherwise strand the old row
        // and take every project link with it.
        await Sync(new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha", "main", false))));
        await Sync(new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha-renamed", "main", false))));

        await using var db = NewContext();
        var repos = await db.GitHubRepositories.IgnoreQueryFilters().ToListAsync();

        Assert.Single(repos);
        Assert.Equal("rigon-org/alpha-renamed", repos[0].FullName);
    }

    [Fact]
    public async SystemTask.Task RemovedRepository_IsSoftDeleted_NotErased()
    {
        await Sync(new FakeApiClient().Enqueue(Page("all",
            (1001, "rigon-org/alpha", "main", false),
            (1002, "rigon-org/beta", "main", false))));

        await Sync(new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha", "main", false))));

        await using var db = NewContext();

        Assert.Single(await db.GitHubRepositories.ToListAsync());

        var gone = await db.GitHubRepositories.IgnoreQueryFilters().SingleAsync(r => r.RepositoryId == 1002);
        Assert.True(gone.IsDeleted);
        Assert.NotNull(gone.DeletedAt);
    }

    [Fact]
    public async SystemTask.Task Sync_FollowsPaginationPastPageOne()
    {
        var api = new FakeApiClient()
            .Enqueue(
                Page("all", (1001, "rigon-org/alpha", "main", false)),
                "<https://api.github.com/installation/repositories?page=2>; rel=\"next\"")
            .Enqueue(Page("all", (1002, "rigon-org/beta", "main", false)));

        var result = await Sync(api);

        Assert.Equal(2, result.Value);
        Assert.Equal(2, api.RequestedUrls.Count);
        Assert.EndsWith("page=2", api.RequestedUrls[1], StringComparison.Ordinal);

        await using var db = NewContext();
        Assert.Equal(2, await db.GitHubRepositories.CountAsync());
    }

    [Fact]
    public async SystemTask.Task Sync_IsIdempotent()
    {
        var page = Page("all",
            (1001, "rigon-org/alpha", "main", false),
            (1002, "rigon-org/beta", "main", false));

        await Sync(new FakeApiClient().Enqueue(page));
        await Sync(new FakeApiClient().Enqueue(page));

        await using var db = NewContext();

        Assert.Equal(2, await db.GitHubRepositories.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await db.GitHubRepositories.CountAsync());
    }

    [Fact]
    public async SystemTask.Task DisconnectThenReconnect_RevivesRepositoryRows_WithoutThrowing()
    {
        // §0m, one step past the installation: disconnect soft-deletes the repositories, but
        // IX_GitHubRepositories_RepositoryId stays unfiltered. A filtered upsert would find
        // nothing, insert, and 500 on the first reconnect.
        await Sync(new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha", "main", false))));

        await using (var db = NewContext())
        {
            foreach (var repository in await db.GitHubRepositories.ToListAsync())
            {
                repository.IsDeleted = true;
                repository.DeletedAt = DateTime.UtcNow;
            }

            var installation = await db.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            installation.DeletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.IgnoreQueryFilters().SingleAsync();
            installation.IsDeleted = false;
            installation.DeletedAt = null;
            await db.SaveChangesAsync();
        }

        var result = await Sync(new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha", "main", false))));

        Assert.True(result.IsSuccess);

        await using (var db = NewContext())
        {
            var repository = await db.GitHubRepositories.SingleAsync();
            Assert.False(repository.IsDeleted);
            Assert.Null(repository.DeletedAt);
        }
    }

    [Fact]
    public async SystemTask.Task ApiFailure_IsAResultFailure_AndChangesNothing()
    {
        await Sync(new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha", "main", false))));

        var failing = new FakeApiClient { Failure = new Error("GitHub.RateLimited", "slow down") };

        var result = await Sync(failing);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.RateLimited", result.Errors[0].Code);

        // Crucially, the pre-existing repository is not soft-deleted just because the listing
        // call failed — an empty response and a failed response must not look the same.
        await using var db = NewContext();
        Assert.Single(await db.GitHubRepositories.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task RepositoryPreviouslyOwnedByAnotherCompany_ChangesHands_AndDropsTheOldLinks()
    {
        // Not in the plan, but reachable through the flow the plan documents in §3: after a
        // GitHub-side uninstall another company can connect the same org, and GitHub returns
        // the same repository ids. The unfiltered unique index allows exactly one row per
        // repository, so ownership must move — and the previous company's project links must
        // not move with it.
        await Sync(new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha", "main", false))));

        int linkId;
        int otherInstallationRowId;

        await using (var db = NewContext())
        {
            var project = new Project { Name = "Old Co Project", Key = "OLD", CompanyId = _companyId };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            var repository = await db.GitHubRepositories.SingleAsync();

            var link = new ProjectRepositoryLink
            {
                ProjectId = project.Id,
                GitHubRepositoryId = repository.Id,
                CompanyId = _companyId,
                LinkedByUserId = "old-user",
            };

            db.ProjectRepositoryLinks.Add(link);

            // The org is reinstalled by a different company, which gets a new installation id.
            var reinstalled = new GitHubInstallation
            {
                InstallationId = 9501,
                CompanyId = _otherCompanyId,
                AccountLogin = "rigon-org",
                AccountType = "Organization",
                RepositorySelection = RepositorySelection.Selected,
            };

            db.GitHubInstallations.Add(reinstalled);
            await db.SaveChangesAsync();

            linkId = link.Id;
            otherInstallationRowId = reinstalled.Id;
        }

        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);
            var reinstalled = await db.GitHubInstallations.SingleAsync(i => i.InstallationId == 9501);

            var api = new FakeApiClient().Enqueue(Page("all", (1001, "rigon-org/alpha", "main", false)));

            var result = await new GitHubRepositorySyncService(api, uow).SyncAsync(reinstalled);

            Assert.True(result.IsSuccess);
        }

        await using (var db = NewContext())
        {
            var repository = await db.GitHubRepositories.IgnoreQueryFilters().SingleAsync(r => r.RepositoryId == 1001);

            Assert.Equal(_otherCompanyId, repository.CompanyId);
            Assert.Equal(otherInstallationRowId, repository.GitHubInstallationId);
            Assert.False(repository.IsDeleted);

            var link = await db.ProjectRepositoryLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == linkId);
            Assert.True(link.IsDeleted);
        }
    }
}
