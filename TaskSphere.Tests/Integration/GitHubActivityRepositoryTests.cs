using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubCommit = TaskSphere.Domain.Entities.GitHubCommit;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubPullRequest = TaskSphere.Domain.Entities.GitHubPullRequest;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using SystemTask = System.Threading.Tasks;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using TaskLink = TaskSphere.Domain.Entities.TaskLink;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The lookups the sync upserts depend on. Every GitHub-identity lookup suppresses the query
/// filter, because the unique indexes are unfiltered: a filtered lookup would find nothing,
/// insert, and violate the index on the first resync after a soft delete.
/// </summary>
public class GitHubActivityRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubActivityRepoTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private Guid _otherCompanyId;
    private int _repositoryId;

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

        var company = new Company { Name = "Repo Test Co" };
        var other = new Company { Name = "Other Co" };
        db.Companies.AddRange(company, other);
        await db.SaveChangesAsync();

        _companyId = company.Id;
        _otherCompanyId = other.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 7101,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.Selected,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 8101,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();
        _repositoryId = repository.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task GetBySha_FindsASoftDeletedCommit_SoAResyncRevivesRatherThanCollides()
    {
        await using (var db = NewContext())
        {
            db.GitHubCommits.Add(new GitHubCommit
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Sha = "abc1234abc1234abc1234abc1234abc1234abc12",
                Message = "TS-42",
                AuthorName = "Rigon",
                CommittedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/rigon-org/api/commit/abc1234",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var repo = new GitHubCommitRepository(db);

            Assert.Empty(await repo.GetByRepository(_companyId, _repositoryId).ToListAsync());

            var found = await repo.GetByShaIncludingDeletedAsync(
                _repositoryId, "abc1234abc1234abc1234abc1234abc1234abc12", default);

            Assert.NotNull(found);
            Assert.True(found!.IsDeleted);
        }
    }

    [Fact]
    public async SystemTask.Task GetByName_FindsASoftDeletedBranch()
    {
        await using (var db = NewContext())
        {
            db.GitHubBranches.Add(new GitHubBranch
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Name = "TS-42-fix",
                HeadSha = "aaa",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var repo = new GitHubBranchRepository(db);

            var found = await repo.GetByNameIncludingDeletedAsync(_repositoryId, "TS-42-fix", default);

            Assert.NotNull(found);
            Assert.True(found!.IsDeleted);

            // GetByCompanyIncludingDeleted is the panel's read of every branch, deleted ones
            // included — it needs the same filter suppression, scoped to the same company.
            var companyBranches = await repo.GetByCompanyIncludingDeleted(_companyId).ToListAsync();
            Assert.Contains(companyBranches, b => b.Name == "TS-42-fix" && b.IsDeleted);
            Assert.Empty(await repo.GetByCompanyIncludingDeleted(Guid.NewGuid()).ToListAsync());
        }
    }

    [Fact]
    public async SystemTask.Task GetByNumber_FindsASoftDeletedPullRequest()
    {
        await using (var db = NewContext())
        {
            db.GitHubPullRequests.Add(new GitHubPullRequest
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Number = 42,
                Title = "TS-42 fix",
                State = PullRequestState.Closed,
                AuthorLogin = "rigon",
                HeadBranch = "TS-42-fix",
                OpenedAtUtc = DateTime.UtcNow,
                GitHubUpdatedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/rigon-org/api/pull/42",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var repo = new GitHubPullRequestRepository(db);

            var found = await repo.GetByNumberIncludingDeletedAsync(_repositoryId, 42, default);

            Assert.NotNull(found);
            Assert.True(found!.IsDeleted);
        }
    }

    [Fact]
    public async SystemTask.Task GetByRepository_NeverReturnsAnotherCompanysRows()
    {
        await using (var db = NewContext())
        {
            db.GitHubCommits.AddRange(
                new GitHubCommit
                {
                    GitHubRepositoryId = _repositoryId,
                    CompanyId = _companyId,
                    Sha = "1111111111111111111111111111111111111111",
                    Message = "mine",
                    AuthorName = "Rigon",
                    CommittedAtUtc = DateTime.UtcNow,
                    HtmlUrl = "https://github.com/x/1",
                },
                // Same repository row, wrong company: exactly what a botched companyId
                // predicate would let through.
                new GitHubCommit
                {
                    GitHubRepositoryId = _repositoryId,
                    CompanyId = _otherCompanyId,
                    Sha = "2222222222222222222222222222222222222222",
                    Message = "theirs",
                    AuthorName = "Someone",
                    CommittedAtUtc = DateTime.UtcNow,
                    HtmlUrl = "https://github.com/x/2",
                });

            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var repo = new GitHubCommitRepository(db);

            var mine = await repo.GetByCompany(_companyId).ToListAsync();

            // Positive as well as negative: an implementation returning nothing at all would
            // satisfy the "no leak" assertion on its own.
            Assert.Single(mine);
            Assert.Equal("mine", mine[0].Message);
            Assert.Empty(await repo.GetByCompany(Guid.NewGuid()).ToListAsync());

            // The method this test is named for: GetByRepository must apply the same
            // companyId scoping GetByCompany does.
            var mineInRepo = await repo.GetByRepository(_companyId, _repositoryId).ToListAsync();
            Assert.Single(mineInRepo);
            Assert.Equal("mine", mineInRepo[0].Message);
            Assert.Empty(await repo.GetByRepository(Guid.NewGuid(), _repositoryId).ToListAsync());
        }
    }

    [Fact]
    public async SystemTask.Task GetByCompany_And_GetByRepository_ExcludeSoftDeletedRows()
    {
        int taskId;
        await using (var db = NewContext())
        {
            var task = new TaskEntity { Title = "Host task", CompanyId = _companyId };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;

            db.GitHubCommits.Add(new GitHubCommit
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Sha = "3333333333333333333333333333333333333333",
                Message = "gone",
                AuthorName = "Rigon",
                CommittedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/x/3",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
            });
            db.GitHubBranches.Add(new GitHubBranch
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Name = "gone-branch",
                HeadSha = "ccc",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
            });
            db.GitHubPullRequests.Add(new GitHubPullRequest
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Number = 99,
                Title = "gone pr",
                State = PullRequestState.Closed,
                AuthorLogin = "rigon",
                HeadBranch = "gone-branch",
                OpenedAtUtc = DateTime.UtcNow,
                GitHubUpdatedAtUtc = DateTime.UtcNow,
                HtmlUrl = "https://github.com/x/pull/99",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
            });
            db.TaskLinks.Add(new TaskLink
            {
                CompanyId = _companyId,
                TaskId = taskId,
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        // A stray IgnoreQueryFilters() on any of these would leak the soft-deleted row back
        // in — the exact opposite failure from the IncludingDeleted lookups above.
        await using (var db = NewContext())
        {
            Assert.Empty(await new GitHubCommitRepository(db).GetByCompany(_companyId).ToListAsync());
            Assert.Empty(await new GitHubCommitRepository(db).GetByRepository(_companyId, _repositoryId).ToListAsync());

            Assert.Empty(await new GitHubBranchRepository(db).GetByCompany(_companyId).ToListAsync());
            Assert.Empty(await new GitHubBranchRepository(db).GetByRepository(_companyId, _repositoryId).ToListAsync());

            Assert.Empty(await new GitHubPullRequestRepository(db).GetByCompany(_companyId).ToListAsync());
            Assert.Empty(await new GitHubPullRequestRepository(db).GetByRepository(_companyId, _repositoryId).ToListAsync());

            Assert.Empty(await new TaskLinkRepository(db).GetByCompany(_companyId).ToListAsync());
            Assert.Empty(await new TaskLinkRepository(db).GetByTask(_companyId, taskId).ToListAsync());
        }
    }

    [Fact]
    public async SystemTask.Task UnitOfWork_ExposesAllFourRepositories()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        Assert.NotNull(uow.GitHubCommits);
        Assert.NotNull(uow.GitHubBranches);
        Assert.NotNull(uow.GitHubPullRequests);
        Assert.NotNull(uow.TaskLinks);

        // Lazy, and cached: the same instance on a second read, like every other property.
        Assert.Same(uow.GitHubCommits, uow.GitHubCommits);
        Assert.Same(uow.GitHubBranches, uow.GitHubBranches);
        Assert.Same(uow.GitHubPullRequests, uow.GitHubPullRequests);
        Assert.Same(uow.TaskLinks, uow.TaskLinks);
    }

    private async SystemTask.Task<(int BranchId, int CommitId)> SeedBranchAndCommit(ApplicationDbContext db)
    {
        var branch = new TaskSphere.Domain.Entities.GitHubBranch
        {
            CompanyId = _companyId,
            GitHubRepositoryId = _repositoryId,
            Name = "TS-42-login",
            HeadSha = "bbb",
        };

        var commit = new TaskSphere.Domain.Entities.GitHubCommit
        {
            CompanyId = _companyId,
            GitHubRepositoryId = _repositoryId,
            Sha = "ahead1",
            Message = "wire up the login form",
            AuthorName = "Rigon",
            CommittedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/commit/ahead1",
        };

        db.GitHubBranches.Add(branch);
        db.GitHubCommits.Add(commit);
        await db.SaveChangesAsync();

        return (branch.Id, commit.Id);
    }

    [Fact]
    public async SystemTask.Task GitHubBranchCommits_ExistsForPair_IsTrueOnlyForTheExactPair()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        var (branchId, commitId) = await SeedBranchAndCommit(db);

        await uow.GitHubBranchCommits.AddAsync(new TaskSphere.Domain.Entities.GitHubBranchCommit
        {
            CompanyId = _companyId,
            GitHubBranchId = branchId,
            GitHubCommitId = commitId,
        });
        await uow.SaveChangesAsync(default);

        Assert.True(await uow.GitHubBranchCommits.ExistsForPairAsync(branchId, commitId));

        // The dangerous direction: a transposed or partially-matching pair must NOT read as
        // present, or the sync stops writing rows it should write.
        Assert.False(await uow.GitHubBranchCommits.ExistsForPairAsync(commitId, branchId));
        Assert.False(await uow.GitHubBranchCommits.ExistsForPairAsync(branchId, commitId + 1));
        Assert.False(await uow.GitHubBranchCommits.ExistsForPairAsync(branchId + 1, commitId));
    }

    [Fact]
    public async SystemTask.Task GitHubBranchCommits_GetByCompany_ExcludesOtherCompanies()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        var (branchId, commitId) = await SeedBranchAndCommit(db);

        await uow.GitHubBranchCommits.AddAsync(new TaskSphere.Domain.Entities.GitHubBranchCommit
        {
            CompanyId = _companyId,
            GitHubBranchId = branchId,
            GitHubCommitId = commitId,
        });
        await uow.SaveChangesAsync(default);

        Assert.Single(await uow.GitHubBranchCommits.GetByCompany(_companyId).ToListAsync());
        Assert.Empty(await uow.GitHubBranchCommits.GetByCompany(_otherCompanyId).ToListAsync());
    }
}
