using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubCommit = TaskSphere.Domain.Entities.GitHubCommit;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using SystemTask = System.Threading.Tasks;

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
    }
}
