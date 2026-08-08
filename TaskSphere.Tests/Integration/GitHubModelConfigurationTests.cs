using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

// Aliased for the same reason as the other integration tests: importing
// TaskSphere.Domain.Entities wholesale would make every bare `Task` in this file
// ambiguous with System.Threading.Tasks.Task.
using Company = TaskSphere.Domain.Entities.Company;
using Project = TaskSphere.Domain.Entities.Project;

namespace TaskSphere.Tests.Integration;

public class GitHubModelConfigurationTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubModelTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _projectId;
    private int _installationId;
    private int _repositoryId;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    public async System.Threading.Tasks.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "GitHub Model Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 5001,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.Selected,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();
        _installationId = installation.Id;

        var repository = new GitHubRepository
        {
            RepositoryId = 9001,
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

    public System.Threading.Tasks.Task DisposeAsync() => System.Threading.Tasks.Task.CompletedTask;

    [Fact]
    public async System.Threading.Tasks.Task SoftDeletedInstallation_IsHiddenByQueryFilter_AndVisibleWhenIgnored()
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
            Assert.Empty(await db.GitHubInstallations.ToListAsync());
            Assert.Single(await db.GitHubInstallations.IgnoreQueryFilters().ToListAsync());
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task SoftDeletedRepository_IsHiddenByQueryFilter_AndVisibleWhenIgnored()
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
            Assert.Empty(await db.GitHubRepositories.ToListAsync());
            Assert.Single(await db.GitHubRepositories.IgnoreQueryFilters().ToListAsync());
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task SoftDeletedLink_IsHiddenByQueryFilter_AndVisibleWhenIgnored()
    {
        await using (var db = NewContext())
        {
            var link = await db.ProjectRepositoryLinks.SingleAsync();
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            Assert.Empty(await db.ProjectRepositoryLinks.ToListAsync());
            Assert.Single(await db.ProjectRepositoryLinks.IgnoreQueryFilters().ToListAsync());
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task DuplicateInstallationId_IsRejected_EvenWhenTheExistingRowIsSoftDeleted()
    {
        // The unique index is deliberately unfiltered: GitHub never recycles installation
        // ids, and leaving the filter off stops another company claiming a used one.
        await using (var db = NewContext())
        {
            var installation = await db.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.GitHubInstallations.Add(new GitHubInstallation
            {
                InstallationId = 5001,
                CompanyId = _companyId,
                AccountLogin = "other-org",
                AccountType = "Organization",
            });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_GitHubInstallations_InstallationId", ex.GetBaseException().Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task UnlinkThenRelink_IsAllowed_BecauseTheLinkIndexIsFiltered()
    {
        await using (var db = NewContext())
        {
            var link = await db.ProjectRepositoryLinks.SingleAsync();
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                ProjectId = _projectId,
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "linker-user-id",
            });

            await db.SaveChangesAsync();

            Assert.Single(await db.ProjectRepositoryLinks.ToListAsync());
            Assert.Equal(2, await db.ProjectRepositoryLinks.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task IgnoreQueryFilters_SuppressesSoftDeleteOnNavigations_Too()
    {
        // The reason Task 4's IgnoreQueryFilters lookups must return bare entities with no
        // Include(): suppression is query-wide, so an Include would happily materialize a
        // soft-deleted company. This test pins that behaviour so the constraint has a reason
        // a future reader can see.
        await using (var db = NewContext())
        {
            var company = await db.Companies.SingleAsync();
            company.IsDeleted = true;
            company.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var withFilters = await db.GitHubInstallations
                .Include(i => i.Company)
                .SingleOrDefaultAsync();

            Assert.Null(withFilters);

            var ignored = await db.GitHubInstallations
                .IgnoreQueryFilters()
                .Include(i => i.Company)
                .SingleAsync();

            Assert.NotNull(ignored.Company);
            Assert.True(ignored.Company!.IsDeleted);
        }
    }
}
