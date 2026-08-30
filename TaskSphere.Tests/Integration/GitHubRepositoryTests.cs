using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;

using Company = TaskSphere.Domain.Entities.Company;
using Project = TaskSphere.Domain.Entities.Project;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class GitHubRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubRepoTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private Guid _otherCompanyId;
    private int _projectId;
    private int _installationId;
    private int _repositoryId;

    private const long GitHubInstallationId = 7001;
    private const long GitHubRepoId = 8001;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static IUnitOfWork NewUnitOfWork(ApplicationDbContext context) => new UnitOfWork(context);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Repo Test Co" };
        var otherCompany = new Company { Name = "Other Co" };
        db.Companies.AddRange(company, otherCompany);
        await db.SaveChangesAsync();
        _companyId = company.Id;
        _otherCompanyId = otherCompany.Id;

        var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = GitHubInstallationId,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();
        _installationId = installation.Id;

        var repository = new GitHubRepository
        {
            RepositoryId = GitHubRepoId,
            GitHubInstallationId = _installationId,
            CompanyId = _companyId,
            FullName = "rigon-org/tasksphere",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();
        _repositoryId = repository.Id;

        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            ProjectId = _projectId,
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            LinkedByUserId = "linker-user-id",
        });
        await db.SaveChangesAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task UnitOfWork_ExposesAllThreeGitHubRepositories()
    {
        await using var db = NewContext();
        var uow = NewUnitOfWork(db);

        Assert.NotNull(uow.GitHubInstallations);
        Assert.NotNull(uow.GitHubRepositories);
        Assert.NotNull(uow.ProjectRepositoryLinks);

        // Lazily initialised properties must return the same instance on repeat access,
        // matching the existing UnitOfWork members.
        Assert.Same(uow.GitHubInstallations, uow.GitHubInstallations);
    }

    [Fact]
    public async SystemTask.Task Queries_AreScopedByCompany()
    {
        await using var db = NewContext();
        var uow = NewUnitOfWork(db);

        Assert.NotNull(await uow.GitHubInstallations.GetByCompanyAsync(_companyId));
        Assert.Null(await uow.GitHubInstallations.GetByCompanyAsync(_otherCompanyId));

        Assert.Single(await uow.GitHubRepositories.GetByCompany(_companyId).ToListAsync());
        Assert.Empty(await uow.GitHubRepositories.GetByCompany(_otherCompanyId).ToListAsync());

        Assert.Single(await uow.ProjectRepositoryLinks.GetByProject(_companyId, _projectId).ToListAsync());
        Assert.Empty(await uow.ProjectRepositoryLinks.GetByProject(_otherCompanyId, _projectId).ToListAsync());
    }

    [Fact]
    public async SystemTask.Task GetByGitHubIdIncludingDeleted_FindsASoftDeletedInstallation()
    {
        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            installation.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var uow = NewUnitOfWork(db);

            // The filtered lookup misses it — which is exactly the path that would insert
            // and then collide with the unfiltered unique index.
            Assert.Null(await uow.GitHubInstallations.GetByCompanyAsync(_companyId));

            var found = await uow.GitHubInstallations.GetByGitHubIdIncludingDeletedAsync(GitHubInstallationId);
            Assert.NotNull(found);
            Assert.True(found!.IsDeleted);
        }
    }

    [Fact]
    public async SystemTask.Task GetByGitHubIdIncludingDeleted_FindsASoftDeletedRepository()
    {
        await using (var db = NewContext())
        {
            var repository = await db.GitHubRepositories.SingleAsync();
            repository.IsDeleted = true;
            repository.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var uow = NewUnitOfWork(db);

            Assert.Empty(await uow.GitHubRepositories.GetByCompany(_companyId).ToListAsync());

            var found = await uow.GitHubRepositories.GetByGitHubIdIncludingDeletedAsync(GitHubRepoId);
            Assert.NotNull(found);
            Assert.True(found!.IsDeleted);
        }
    }

    [Fact]
    public async SystemTask.Task GetByGitHubIdIncludingDeleted_LoadsNoNavigations_SoSoftDeleteIsNotSuppressedElsewhere()
    {
        // IgnoreQueryFilters() is query-wide. If these lookups ever gain an Include(), a
        // soft-deleted Company would materialize alongside the installation and leak into
        // whatever the caller does next. Returning a bare entity is what prevents that.
        await using (var db = NewContext())
        {
            var company = await db.Companies.SingleAsync(c => c.Id == _companyId);
            company.IsDeleted = true;
            company.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var installation = await NewUnitOfWork(db)
                .GitHubInstallations.GetByGitHubIdIncludingDeletedAsync(GitHubInstallationId);

            Assert.NotNull(installation);
            Assert.Null(installation!.Company);
        }

        await using (var db = NewContext())
        {
            var repository = await NewUnitOfWork(db)
                .GitHubRepositories.GetByGitHubIdIncludingDeletedAsync(GitHubRepoId);

            Assert.NotNull(repository);
            Assert.Null(repository!.Installation);
        }
    }

    [Fact]
    public async SystemTask.Task IncludingDeletedLookups_StillGetNavigationsWiredByChangeTrackerFixup()
    {
        // Documents the limit of the "bare entity, no Include()" rule: it controls what the
        // query loads, not what EF attaches afterwards. Loading the installation and then a
        // repository in the SAME context makes EF fix up repository.Installation to the
        // already-tracked, soft-deleted installation — no Include involved.
        //
        // So the rule prevents a lookup from *pulling in* filtered-out rows; it does not stop
        // a caller that deliberately loads two related things in one context from seeing them
        // joined up. Callers reviving a row should use a short-lived scope.
        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            installation.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var uow = NewUnitOfWork(db);

            var installation = await uow.GitHubInstallations.GetByGitHubIdIncludingDeletedAsync(GitHubInstallationId);
            Assert.NotNull(installation);

            var repository = await uow.GitHubRepositories.GetByGitHubIdIncludingDeletedAsync(GitHubRepoId);

            Assert.NotNull(repository);
            Assert.NotNull(repository!.Installation);
            Assert.True(repository.Installation!.IsDeleted);
        }
    }

    [Fact]
    public async SystemTask.Task GetLinkAsync_IsScopedByCompanyAndHidesSoftDeletedLinks()
    {
        await using (var db = NewContext())
        {
            var uow = NewUnitOfWork(db);

            Assert.NotNull(await uow.ProjectRepositoryLinks.GetLinkAsync(_companyId, _projectId, _repositoryId));
            Assert.Null(await uow.ProjectRepositoryLinks.GetLinkAsync(_otherCompanyId, _projectId, _repositoryId));
        }

        await using (var db = NewContext())
        {
            var link = await db.ProjectRepositoryLinks.SingleAsync();
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var uow = NewUnitOfWork(db);
            Assert.Null(await uow.ProjectRepositoryLinks.GetLinkAsync(_companyId, _projectId, _repositoryId));
        }
    }
}
