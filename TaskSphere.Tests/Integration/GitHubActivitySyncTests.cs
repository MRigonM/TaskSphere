using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
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
    private int _projectId;
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
        _projectId = project.Id;

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
    /// Keyed by URL substring rather than a queue: once commits join the pass the call order
    /// is data-dependent, and a queue would couple every test to it. Captures each requested
    /// URL and the installation id it was sent under — the first is how "only linked
    /// repositories are fetched" is asserted, the second is how "the token is minted for this
    /// company's installation" is.
    /// </summary>
    private sealed class FakeApiClient : IGitHubApiClient
    {
        private readonly List<(string Match, string Body)> _responses = new();
        private readonly Dictionary<string, Error> _failures = new(StringComparer.Ordinal);

        public List<string> RequestedUrls { get; } = new();

        public List<long> RequestedInstallationIds { get; } = new();

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
            RequestedInstallationIds.Add(installationId);

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

    private static string Commits(params (string Sha, string Message, string? Login)[] commits)
        => "[" + string.Join(",", commits.Select(c =>
            "{\"sha\":\"" + c.Sha + "\"," +
            "\"html_url\":\"https://github.com/rigon-org/api/commit/" + c.Sha + "\"," +
            "\"commit\":{\"message\":" + JsonEncode(c.Message) + "," +
            "\"author\":{\"name\":\"Rigon\",\"date\":\"2026-08-11T10:00:00Z\"}}," +
            "\"author\":" + (c.Login is null ? "null" : "{\"login\":\"" + c.Login + "\"}") + "}")) + "]";

    private static string JsonEncode(string value) => System.Text.Json.JsonSerializer.Serialize(value);

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
        var result = await Sync(new FakeApiClient().On("/branches", Branches(("TS-42-fix", "bbb"))));

        // The count the caller is shown comes from the resolver run, not from a constant: the
        // "N links created" line on the sync screen is the only feedback that it did anything.
        Assert.Equal(1, result.Value!.LinksCreated);

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

    [Fact]
    public async SystemTask.Task Commits_AreFetchedOncePerBranch_WithTheWindowOnTheSinceParameter()
    {
        var api = new FakeApiClient()
            .On("/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb")))
            .On("/commits", Commits(("1111111111111111111111111111111111111111", "TS-42 wire it", "MRigonM")));

        await Sync(api);

        var commitCalls = api.RequestedUrls.Where(u => u.Contains("/commits?", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, commitCalls.Count);
        Assert.Contains(commitCalls, u => u.Contains("sha=main", StringComparison.Ordinal));
        Assert.Contains(commitCalls, u => u.Contains("sha=TS-42-fix", StringComparison.Ordinal));

        // The window, not a watermark. Asserted as a parsed date so a format change is a
        // failure rather than a silently different request.
        foreach (var call in commitCalls)
        {
            var since = call.Split("since=")[1].Split('&')[0];
            var parsed = DateTime.Parse(Uri.UnescapeDataString(since), null, System.Globalization.DateTimeStyles.AdjustToUniversal);

            Assert.InRange(DateTime.UtcNow - parsed, TimeSpan.FromDays(29.9), TimeSpan.FromDays(30.1));
        }
    }

    [Fact]
    public async SystemTask.Task ABranchNameWithASlash_IsEscapedIntoTheCommitsUrl()
    {
        // feature/TS-42-fix is the branch shape this repo actually uses; an unescaped slash
        // would request a different repository path entirely.
        var api = new FakeApiClient()
            .On("/branches", Branches(("feature/TS-42-fix", "bbb")))
            .On("/commits", "[]");

        await Sync(api);

        var call = Assert.Single(api.RequestedUrls.Where(u => u.Contains("/commits?", StringComparison.Ordinal)));

        Assert.Contains("sha=feature%2FTS-42-fix", call, StringComparison.Ordinal);
    }

    [Fact]
    public async SystemTask.Task Commits_AreUpserted_WithMessageAuthorAndUrl()
    {
        var api = new FakeApiClient()
            .On("/branches", Branches(("main", "aaa")))
            .On("/commits", Commits(
                ("1111111111111111111111111111111111111111", "TS-42 wire the panel", "MRigonM"),
                ("2222222222222222222222222222222222222222", "chore: tidy", null)));

        var result = await Sync(api);

        Assert.Equal(2, result.Value!.Commits);

        await using var db = NewContext();
        var commits = await db.GitHubCommits.OrderBy(c => c.Sha).ToListAsync();

        Assert.Equal(2, commits.Count);
        Assert.Equal("TS-42 wire the panel", commits[0].Message);
        Assert.Equal("Rigon", commits[0].AuthorName);
        Assert.Equal("MRigonM", commits[0].AuthorLogin);
        Assert.Equal(new DateTime(2026, 8, 11, 10, 0, 0), commits[0].CommittedAtUtc);
        Assert.EndsWith("/commit/1111111111111111111111111111111111111111", commits[0].HtmlUrl, StringComparison.Ordinal);

        // GitHub could not match the second commit to an account; the git author still stands.
        Assert.Null(commits[1].AuthorLogin);
        Assert.Equal("Rigon", commits[1].AuthorName);
    }

    [Fact]
    public async SystemTask.Task ACommitReachableFromTwoBranches_IsStoredOnce()
    {
        var api = new FakeApiClient()
            .On("/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb")))
            .On("/commits", Commits(("1111111111111111111111111111111111111111", "TS-42 wire it", "MRigonM")));

        var result = await Sync(api);

        // Both branch listings return the same commit; the natural key collapses them.
        Assert.Equal(1, result.Value!.Commits);

        await using var db = NewContext();
        Assert.Single(await db.GitHubCommits.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ReSyncingTheSameCommits_CreatesNoDuplicates()
    {
        var api = () => new FakeApiClient()
            .On("/branches", Branches(("main", "aaa")))
            .On("/commits", Commits(("1111111111111111111111111111111111111111", "TS-42 wire it", "MRigonM")));

        await Sync(api());
        await Sync(api());

        await using var db = NewContext();

        Assert.Equal(1, await db.GitHubCommits.IgnoreQueryFilters().CountAsync());
        Assert.Single(await db.TaskLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task ACommitMessageNamingATask_LinksThroughTheResolver()
    {
        var api = new FakeApiClient()
            .On("/branches", Branches(("main", "aaa")))
            .On("/commits", Commits(("1111111111111111111111111111111111111111", "fixes TS-42 at last", "MRigonM")));

        var result = await Sync(api);

        Assert.Equal(1, result.Value!.LinksCreated);

        await using var db = NewContext();
        var link = await db.TaskLinks.SingleAsync();

        Assert.Equal(_ts42TaskId, link.TaskId);
        Assert.NotNull(link.GitHubCommitId);
    }

    // ---- what the reads must not see ------------------------------------------------------

    [Fact]
    public async SystemTask.Task ASoftDeletedRepository_IsNotFetched()
    {
        // The leak direction nothing downstream can catch: the panel renders deleted branches
        // by design, so branches pulled for a disconnected repository would look like data.
        await using (var db = NewContext())
        {
            var repository = await db.GitHubRepositories.SingleAsync(r => r.Id == _apiRepositoryId);
            repository.IsDeleted = true;
            repository.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var api = new FakeApiClient().On("/branches", Branches(("main", "aaa")));

        var result = await Sync(api);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.RepositoriesSynced);
        Assert.Empty(api.RequestedUrls);

        await using (var db = NewContext())
        {
            Assert.Empty(await db.GitHubBranches.IgnoreQueryFilters().ToListAsync());

            // Nothing was looked at, so the panel must not claim a time at which it was.
            Assert.Null((await db.GitHubInstallations.SingleAsync()).ActivitySyncedAtUtc);
        }
    }

    [Fact]
    public async SystemTask.Task AnAlreadySoftDeletedBranch_KeepsTheTimeItWentAway()
    {
        await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb"))));
        await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa"))));

        DateTime? wentAwayAt;
        await using (var db = NewContext())
            wentAwayAt = (await db.GitHubBranches.IgnoreQueryFilters()
                .SingleAsync(b => b.Name == "TS-42-fix")).DeletedAt;

        await Sync(new FakeApiClient().On("/branches", Branches(("main", "aaa"))));

        await using (var db = NewContext())
        {
            var gone = await db.GitHubBranches.IgnoreQueryFilters().SingleAsync(b => b.Name == "TS-42-fix");

            // If the absent pass read past the soft-delete filter it would re-mark this row on
            // every sync, and DeletedAt would answer "when did we last look" instead of
            // "when did the branch go away".
            Assert.Equal(wentAwayAt, gone.DeletedAt);
        }
    }

    [Fact]
    public async SystemTask.Task ALinkRowBelongingToAnotherCompany_ReachesNothing_InEitherDirection()
    {
        // Both halves of the cross-tenant shape at once: a link of ours pointing at their
        // repository, and a link of theirs pointing at ours. Company scope is on both reads
        // because dropping either one is enough to sync a repository under the wrong token.
        await using (var db = NewContext())
        {
            var other = new Company { Name = "Other Co" };
            db.Companies.Add(other);
            await db.SaveChangesAsync();

            var otherProject = new Project { Name = "Theirs", Key = "OT", CompanyId = other.Id };
            db.Projects.Add(otherProject);

            var otherInstallation = new GitHubInstallation
            {
                InstallationId = 9901,
                CompanyId = other.Id,
                AccountLogin = "other-org",
                AccountType = "Organization",
                RepositorySelection = RepositorySelection.All,
            };
            db.GitHubInstallations.Add(otherInstallation);
            await db.SaveChangesAsync();

            var secret = new GitHubRepository
            {
                RepositoryId = 8401,
                GitHubInstallationId = otherInstallation.Id,
                CompanyId = other.Id,
                FullName = "other-org/secret",
                DefaultBranch = "main",
            };
            db.GitHubRepositories.Add(secret);
            await db.SaveChangesAsync();

            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                ProjectId = _projectId,
                GitHubRepositoryId = secret.Id,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                ProjectId = otherProject.Id,
                GitHubRepositoryId = _webRepositoryId,
                CompanyId = other.Id,
                LinkedByUserId = "them",
            });
            await db.SaveChangesAsync();
        }

        var api = new FakeApiClient().On("/branches", Branches(("main", "aaa")));

        var result = await Sync(api);

        Assert.Equal(1, result.Value!.RepositoriesSynced);
        // Verify only our company's repository was requested; check no URL mentions "other-org"
        Assert.DoesNotContain(api.RequestedUrls, u => u.Contains("other-org", StringComparison.Ordinal));
        Assert.Contains(api.RequestedUrls, u => u.Contains("rigon-org/api", StringComparison.Ordinal));
    }

    [Fact]
    public async SystemTask.Task ABranchLeftBehindByAnOwnershipMove_IsRetaggedToTheCurrentCompany()
    {
        // GitHubRepositorySyncService.UpsertAsync moves a repository's CompanyId when a second
        // company connects the same org after an uninstall, and soft-deletes the old links.
        // It does not touch GitHubBranches, so the rows arrive here still tagged to the
        // previous owner — and the name lookup carries no company scope, so it finds them.
        Guid previousOwnerId;
        await using (var db = NewContext())
        {
            var previousOwner = new Company { Name = "Previous Owner" };
            db.Companies.Add(previousOwner);
            await db.SaveChangesAsync();
            previousOwnerId = previousOwner.Id;

            db.GitHubBranches.Add(new GitHubBranch
            {
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = previousOwnerId,
                Name = "main",
                HeadSha = "stale111",
            });
            await db.SaveChangesAsync();
        }

        await Sync(new FakeApiClient().On("/branches", Branches(("main", "fresh222"))));

        await using (var db = NewContext())
        {
            var branch = await db.GitHubBranches.IgnoreQueryFilters().SingleAsync();

            Assert.Equal(_companyId, branch.CompanyId);
            Assert.Equal("fresh222", branch.HeadSha);
        }
    }

    // ---- the request ----------------------------------------------------------------------

    [Fact]
    public async SystemTask.Task TheBranchesCall_GoesOutUnderThisCompanysInstallation_AndAsksForAFullPage()
    {
        var api = new FakeApiClient().On("/branches", Branches(("main", "aaa")));

        await Sync(api);

        // A wrong installation id is a token minted for someone else's account, not a counter
        // bug; per_page is what keeps a 31-branch repository from having 70 branches marked
        // absent on the strength of GitHub's default page size.
        Assert.All(api.RequestedInstallationIds, id => Assert.Equal(TheInstallationId, id));
        var branchesUrl = Assert.Single(api.RequestedUrls.Where(u => u.Contains("/branches", StringComparison.Ordinal)));
        Assert.Contains("per_page=100", branchesUrl, StringComparison.Ordinal);
    }

    // ---- what the parser must survive ------------------------------------------------------

    [Fact]
    public async SystemTask.Task ABranchWithNoName_IsSkipped_AndNotCounted()
    {
        var api = new FakeApiClient().On(
            "/branches",
            "[{\"name\":\"main\",\"commit\":{\"sha\":\"aaa\"}},{\"commit\":{\"sha\":\"bbb\"}}]");

        var result = await Sync(api);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Failures);

        // Counted from the branches actually written, not from the payload length: Name is
        // NOT NULL, so a nameless entry is a row that was never going to exist.
        Assert.Equal(1, result.Value.Branches);

        await using var db = NewContext();
        Assert.Equal("main", (await db.GitHubBranches.SingleAsync()).Name);
    }

    [Fact]
    public async SystemTask.Task AnUnreadableBranchesResponse_FailsThatRepository_NotSilently()
    {
        var result = await Sync(new FakeApiClient().On("/branches", "{ not json"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.RepositoriesSynced);

        var failure = Assert.Single(result.Value.Failures);
        Assert.Equal("rigon-org/api", failure.RepositoryFullName);
        Assert.Contains("unreadable", failure.Reason, StringComparison.Ordinal);

        await using var db = NewContext();
        Assert.Null((await db.GitHubInstallations.SingleAsync()).ActivitySyncedAtUtc);
    }

    [Fact]
    public async SystemTask.Task ABranchesBodyOfNull_FailsThatRepository_NotSilently()
    {
        var result = await Sync(new FakeApiClient().On("/branches", "null"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.RepositoriesSynced);
        Assert.Contains("no branches list", Assert.Single(result.Value.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async SystemTask.Task ABranchRenamedOnlyByCase_StaysLive()
    {
        // GitHubBranches.Name is case-insensitively collated, so "Feature-X" and "feature-x"
        // are one row. Matching payload names ordinally against stored ones soft-deleted the
        // branch on the same pass that updated it, and the panel then showed it as gone.
        await Sync(new FakeApiClient().On("/branches", Branches(("feature-x", "aaa1111"))));

        var result = await Sync(new FakeApiClient().On("/branches", Branches(("Feature-X", "bbb2222"))));

        Assert.Equal(1, result.Value!.Branches);

        await using var db = NewContext();
        var branch = await db.GitHubBranches.SingleAsync();

        Assert.False(branch.IsDeleted);
        Assert.Equal("bbb2222", branch.HeadSha);
    }

    [Fact]
    public async SystemTask.Task TwoCasingsOfOneBranchName_AreOneRow_NotAnIndexViolation()
    {
        // GitHub can report both; IX_GitHubBranches_RepositoryId_Name cannot hold both. The
        // second has to be dropped in memory, because reaching SaveChanges with it throws
        // DbUpdateException out of the whole run.
        var result = await Sync(new FakeApiClient()
            .On("/branches", Branches(("feature-x", "aaa1111"), ("Feature-X", "bbb2222"))));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Branches);

        await using var db = NewContext();
        Assert.Equal("aaa1111", (await db.GitHubBranches.SingleAsync()).HeadSha);
    }

    // ---- partial success --------------------------------------------------------------------

    [Fact]
    public async SystemTask.Task OneRepositoryFailing_LeavesTheRestSynced_AndIsReported()
    {
        await using (var db = NewContext())
        {
            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                ProjectId = _projectId,
                GitHubRepositoryId = _webRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
            await db.SaveChangesAsync();
        }

        var api = new FakeApiClient()
            .On("/repos/rigon-org/api/branches", Branches(("main", "aaa")))
            .Fail("/repos/rigon-org/web/branches", new Error("GitHub.RateLimited", "Rate limit exceeded."));

        var result = await Sync(api);

        // Partial success is the stated contract on SyncFailureDto: one repository failing is
        // a line in the summary, not a 400 that hides the work the other repositories did.
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.RepositoriesSynced);
        Assert.Equal(1, result.Value.Branches);

        var failure = Assert.Single(result.Value.Failures);
        Assert.Equal("rigon-org/web", failure.RepositoryFullName);
        Assert.Equal("Rate limit exceeded.", failure.Reason);

        await using var db2 = NewContext();
        Assert.NotNull((await db2.GitHubInstallations.SingleAsync()).ActivitySyncedAtUtc);
    }

    [Fact]
    public async SystemTask.Task RepositoriesAreTakenInFullNameOrder()
    {
        // Inserted last, so its row id is the highest and storage order and name order
        // disagree. Failures is a list on a screen; an unordered one reshuffles between runs.
        await using (var db = NewContext())
        {
            var admin = new GitHubRepository
            {
                RepositoryId = 8303,
                GitHubInstallationId = (await db.GitHubInstallations.SingleAsync()).Id,
                CompanyId = _companyId,
                FullName = "rigon-org/admin",
                DefaultBranch = "main",
            };
            db.GitHubRepositories.Add(admin);
            await db.SaveChangesAsync();

            db.ProjectRepositoryLinks.AddRange(
                new ProjectRepositoryLink
                {
                    ProjectId = _projectId,
                    GitHubRepositoryId = admin.Id,
                    CompanyId = _companyId,
                    LinkedByUserId = "rigon",
                },
                new ProjectRepositoryLink
                {
                    ProjectId = _projectId,
                    GitHubRepositoryId = _webRepositoryId,
                    CompanyId = _companyId,
                    LinkedByUserId = "rigon",
                });
            await db.SaveChangesAsync();
        }

        var api = new FakeApiClient();

        await Sync(api);

        // Extract repository names from all URLs (branches and commits); repositories must appear
        // in name order regardless of the order of different endpoint types.
        var repoNames = api.RequestedUrls
            .Select(u => u.Split("/repos/")[1].Split(new[] { "/branches", "/commits" }, StringSplitOptions.None)[0])
            .Distinct()
            .OrderBy(r => r)
            .ToArray();

        Assert.Equal(new[] { "rigon-org/admin", "rigon-org/api", "rigon-org/web" }, repoNames);
    }
}
