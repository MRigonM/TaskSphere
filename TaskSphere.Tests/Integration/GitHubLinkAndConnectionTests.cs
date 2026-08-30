using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Mappings;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

// The entities, not the namespace — TaskSphere.Domain.Entities.Task shadows Task otherwise.
using AppUser = TaskSphere.Domain.Entities.Identity.AppUser;
using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Member = TaskSphere.Domain.Entities.Member;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Tasks 16–18: linking projects to repositories, the read endpoints, and disconnect.
/// <para>
/// Task 17 follows option A for links whose repository row is soft-deleted: they are filtered
/// out of the response and only counted. The accepted consequence is that a repository dropped
/// from the installation leaves its projects permanently, with the count as the only trace.
/// </para>
/// </summary>
public class GitHubLinkAndConnectionTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubLinkTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const long TheInstallationId = 9700;
    private const string MemberUserId = "member-user";
    private const string OutsiderUserId = "outsider-user";

    private Guid _companyId;
    private Guid _otherCompanyId;
    private int _projectId;
    private int _otherCompanyProjectId;
    private int _repositoryId;
    private int _secondRepositoryId;
    private int _otherCompanyRepositoryId;

    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static GitHubProjectLinkService NewLinkService(ApplicationDbContext db)
        => new GitHubProjectLinkService(new UnitOfWork(db), new AccessControlService(db), Mapper);

    /// <summary>
    /// Throws rather than stubs: nothing this class exercises may reach GitHub. A working stub
    /// would let a future change start syncing here and never say so.
    /// </summary>
    private sealed class UnreachableSyncService : IGitHubRepositorySyncService
    {
        public SystemTask.Task<Result<int>> SyncAsync(
            GitHubInstallation installation, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "GitHubLinkAndConnectionTests must never reach the repository sync.");
    }

    private static GitHubConnectionReadService NewReadService(ApplicationDbContext db)
        => new GitHubConnectionReadService(new UnitOfWork(db), Mapper, new UnreachableSyncService());

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Link Test Co" };
        var other = new Company { Name = "Other Co" };
        db.Companies.AddRange(company, other);
        await db.SaveChangesAsync();

        _companyId = company.Id;
        _otherCompanyId = other.Id;

        db.Users.AddRange(
            new AppUser { Id = MemberUserId, Name = "Member", UserName = "member@test.local", Email = "member@test.local" },
            new AppUser { Id = OutsiderUserId, Name = "Outsider", UserName = "outsider@test.local", Email = "outsider@test.local" });
        await db.SaveChangesAsync();

        var project = new Project { Name = "Alpha", Key = "AL", CompanyId = _companyId };
        var otherProject = new Project { Name = "Foreign", Key = "FR", CompanyId = _otherCompanyId };
        db.Projects.AddRange(project, otherProject);
        await db.SaveChangesAsync();

        _projectId = project.Id;
        _otherCompanyProjectId = otherProject.Id;

        db.Members.Add(new Member { ProjectId = _projectId, UserId = MemberUserId });
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = TheInstallationId,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };

        var otherInstallation = new GitHubInstallation
        {
            InstallationId = 9799,
            CompanyId = _otherCompanyId,
            AccountLogin = "foreign-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.Selected,
        };

        db.GitHubInstallations.AddRange(installation, otherInstallation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 5001,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/alpha",
            DefaultBranch = "main",
        };

        var second = new GitHubRepository
        {
            RepositoryId = 5002,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/beta",
            DefaultBranch = "main",
        };

        var foreign = new GitHubRepository
        {
            RepositoryId = 5999,
            GitHubInstallationId = otherInstallation.Id,
            CompanyId = _otherCompanyId,
            FullName = "foreign-org/secret",
            DefaultBranch = "main",
        };

        db.GitHubRepositories.AddRange(repository, second, foreign);
        await db.SaveChangesAsync();

        _repositoryId = repository.Id;
        _secondRepositoryId = second.Id;
        _otherCompanyRepositoryId = foreign.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    // ---- Task 16: link / unlink ---------------------------------------------------------

    [Fact]
    public async SystemTask.Task Link_CreatesTheLink()
    {
        await using var db = NewContext();

        var result = await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        Assert.True(result.IsSuccess);
        Assert.Equal("rigon-org/alpha", result.Value!.FullName);

        await using var verify = NewContext();
        var link = await verify.ProjectRepositoryLinks.SingleAsync();
        Assert.Equal(_projectId, link.ProjectId);
        Assert.Equal(MemberUserId, link.LinkedByUserId);
    }

    [Fact]
    public async SystemTask.Task Link_RepositoryFromAnotherCompany_Is404_AndDoesNotConfirmExistence()
    {
        // A cross-tenant link is the entire reason ProjectRepositoryLink carries CompanyId.
        // NotFound rather than Forbidden so the response doesn't confirm the repo exists.
        await using var db = NewContext();

        var result = await NewLinkService(db)
            .LinkAsync(_companyId, MemberUserId, _projectId, _otherCompanyRepositoryId);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Errors[0].Code);

        await using var verify = NewContext();
        Assert.Empty(await verify.ProjectRepositoryLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async SystemTask.Task Link_ProjectFromAnotherCompany_Is404()
    {
        await using var db = NewContext();

        var result = await NewLinkService(db)
            .LinkAsync(_companyId, MemberUserId, _otherCompanyProjectId, _repositoryId);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Errors[0].Code);
    }

    [Fact]
    public async SystemTask.Task Link_Duplicate_IsIdempotent()
    {
        await using (var db = NewContext())
            await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        await using (var db = NewContext())
        {
            var result = await NewLinkService(db).LinkAsync(_companyId, OutsiderUserId, _projectId, _repositoryId);
            Assert.True(result.IsSuccess);
        }

        await using var verify = NewContext();
        Assert.Single(await verify.ProjectRepositoryLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task Unlink_SoftDeletesTheLink()
    {
        await using (var db = NewContext())
            await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        await using (var db = NewContext())
        {
            var result = await NewLinkService(db).UnlinkAsync(_companyId, _projectId, _repositoryId);
            Assert.True(result.IsSuccess);
        }

        await using var verify = NewContext();
        Assert.Empty(await verify.ProjectRepositoryLinks.ToListAsync());

        var soft = await verify.ProjectRepositoryLinks.IgnoreQueryFilters().SingleAsync();
        Assert.True(soft.IsDeleted);
    }

    [Fact]
    public async SystemTask.Task UnlinkThenRelink_Succeeds()
    {
        // Exercises the filtered unique index from Task 3: IsDeleted = 0 in the filter is what
        // lets a second live row exist alongside the tombstoned one.
        await using (var db = NewContext())
            await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        await using (var db = NewContext())
            await NewLinkService(db).UnlinkAsync(_companyId, _projectId, _repositoryId);

        await using (var db = NewContext())
        {
            var result = await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);
            Assert.True(result.IsSuccess);
        }

        await using var verify = NewContext();
        Assert.Single(await verify.ProjectRepositoryLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task Unlink_WhenNoLinkExists_Is404()
    {
        await using var db = NewContext();

        var result = await NewLinkService(db).UnlinkAsync(_companyId, _projectId, _repositoryId);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Errors[0].Code);
    }

    // ---- Task 17: read endpoints --------------------------------------------------------

    [Fact]
    public async SystemTask.Task GetConnection_ReturnsTheInstallationAndItsLiveRepositories()
    {
        await using var db = NewContext();

        var result = await NewReadService(db).GetConnectionAsync(_companyId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Installation);
        Assert.Equal(TheInstallationId, result.Value.Installation!.InstallationId);
        Assert.Equal(RepositorySelection.All, result.Value.Installation.RepositorySelection);
        Assert.Equal(2, result.Value.Repositories.Count);
    }

    [Fact]
    public async SystemTask.Task GetConnection_ScopedByCompany_NeverLeaksAnotherOrg()
    {
        await using var db = NewContext();

        var result = await NewReadService(db).GetConnectionAsync(_otherCompanyId);

        Assert.True(result.IsSuccess);
        Assert.Equal("foreign-org", result.Value!.Installation!.AccountLogin);
        Assert.Single(result.Value.Repositories);
    }

    [Fact]
    public async SystemTask.Task GetConnection_WhenNothingConnected_ReturnsNoInstallation()
    {
        // §0q: the Connect button is shown whenever no installation is mapped, so "not
        // connected" has to be a successful, empty answer rather than an error.
        await using (var db = NewContext())
        {
            foreach (var installation in await db.GitHubInstallations.ToListAsync())
            {
                installation.IsDeleted = true;
                installation.DeletedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var result = await NewReadService(read).GetConnectionAsync(_companyId);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Installation);
        Assert.Empty(result.Value.Repositories);
    }

    [Fact]
    public async SystemTask.Task GetProjectRepositories_NonMemberUser_IsForbidden_WithTheCode()
    {
        await using (var db = NewContext())
            await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        await using var read = NewContext();

        var result = await NewLinkService(read)
            .GetProjectRepositoriesAsync(_companyId, OutsiderUserId, isCompanyAdmin: false, _projectId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
    }

    [Fact]
    public async SystemTask.Task GetProjectRepositories_Member_SeesTheirProjectsLinks()
    {
        await using (var db = NewContext())
            await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        await using var read = NewContext();

        var result = await NewLinkService(read)
            .GetProjectRepositoriesAsync(_companyId, MemberUserId, isCompanyAdmin: false, _projectId);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Links);
        Assert.Equal("rigon-org/alpha", result.Value.Links[0].FullName);
    }

    [Fact]
    public async SystemTask.Task GetProjectRepositories_CompanyAdmin_BypassesMembership()
    {
        await using (var db = NewContext())
            await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        await using var read = NewContext();

        var result = await NewLinkService(read)
            .GetProjectRepositoriesAsync(_companyId, OutsiderUserId, isCompanyAdmin: true, _projectId);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Links);
    }

    [Fact]
    public async SystemTask.Task GetProjectRepositories_SoftDeletedRepository_IsFilteredOut_AndCounted()
    {
        // Option A. The link row survives — history should not lie — but it is not rendered,
        // and the count is the only signal the user gets.
        await using (var db = NewContext())
        {
            var service = NewLinkService(db);
            await service.LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);
            await service.LinkAsync(_companyId, MemberUserId, _projectId, _secondRepositoryId);
        }

        await using (var db = NewContext())
        {
            var repository = await db.GitHubRepositories.SingleAsync(r => r.Id == _secondRepositoryId);
            repository.IsDeleted = true;
            repository.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();

        var result = await NewLinkService(read)
            .GetProjectRepositoriesAsync(_companyId, MemberUserId, isCompanyAdmin: false, _projectId);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Links);
        Assert.Equal("rigon-org/alpha", result.Value.Links[0].FullName);
        Assert.Equal(1, result.Value.UnavailableCount);

        // The link row itself is untouched.
        await using var verify = NewContext();
        Assert.Equal(2, await verify.ProjectRepositoryLinks.CountAsync());
    }

    [Fact]
    public async SystemTask.Task GetProjectRepositories_NeverReturnsABlankFullName()
    {
        // The default behaviour before this decision: the filtered Include yields a null
        // Repository and the positional-record mapping turns FullName into "". A row with a
        // blank name is worse than either option.
        await using (var db = NewContext())
            await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        await using (var db = NewContext())
        {
            var repository = await db.GitHubRepositories.SingleAsync(r => r.Id == _repositoryId);
            repository.IsDeleted = true;
            repository.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();

        var result = await NewLinkService(read)
            .GetProjectRepositoriesAsync(_companyId, MemberUserId, isCompanyAdmin: false, _projectId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Links);
        Assert.DoesNotContain(result.Value.Links, l => string.IsNullOrWhiteSpace(l.FullName));
        Assert.Equal(1, result.Value.UnavailableCount);
    }

    // ---- Task 18: disconnect ------------------------------------------------------------

    [Fact]
    public async SystemTask.Task Disconnect_SoftDeletesTheInstallationAndItsRepositories()
    {
        await using (var db = NewContext())
        {
            var result = await NewReadService(db).DisconnectAsync(_companyId);
            Assert.True(result.IsSuccess);
        }

        await using var verify = NewContext();

        Assert.Empty(await verify.GitHubInstallations.Where(i => i.CompanyId == _companyId).ToListAsync());
        Assert.Empty(await verify.GitHubRepositories.Where(r => r.CompanyId == _companyId).ToListAsync());

        // Soft, not hard: the rows and their GitHub ids survive so reconnect can revive them.
        Assert.Equal(2, await verify.GitHubRepositories.IgnoreQueryFilters()
            .CountAsync(r => r.CompanyId == _companyId && r.IsDeleted));
    }

    [Fact]
    public async SystemTask.Task Disconnect_LeavesProjectLinksIntact()
    {
        await using (var db = NewContext())
            await NewLinkService(db).LinkAsync(_companyId, MemberUserId, _projectId, _repositoryId);

        await using (var db = NewContext())
            await NewReadService(db).DisconnectAsync(_companyId);

        await using var verify = NewContext();

        // Same principle as immutable task keys: history should not lie.
        Assert.Single(await verify.ProjectRepositoryLinks.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task Disconnect_DoesNotTouchAnotherCompany()
    {
        await using (var db = NewContext())
            await NewReadService(db).DisconnectAsync(_companyId);

        await using var verify = NewContext();

        Assert.Single(await verify.GitHubInstallations.Where(i => i.CompanyId == _otherCompanyId).ToListAsync());
        Assert.Single(await verify.GitHubRepositories.Where(r => r.CompanyId == _otherCompanyId).ToListAsync());
    }

    [Fact]
    public async SystemTask.Task Disconnect_WhenNothingConnected_Is404()
    {
        await using (var db = NewContext())
            await NewReadService(db).DisconnectAsync(_companyId);

        await using var db2 = NewContext();
        var result = await NewReadService(db2).DisconnectAsync(_companyId);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Errors[0].Code);
    }

    // ---- Company-wide links read --------------------------------------------------------

    [Fact]
    public async SystemTask.Task GetByCompany_ReturnsEveryLinkInTheCompany_AndNothingFromAnother()
    {
        await using var seed = NewContext();
        seed.ProjectRepositoryLinks.AddRange(
            new ProjectRepositoryLink
            {
                ProjectId = _projectId,
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                LinkedByUserId = MemberUserId,
            },
            new ProjectRepositoryLink
            {
                ProjectId = _projectId,
                GitHubRepositoryId = _secondRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = MemberUserId,
            },
            new ProjectRepositoryLink
            {
                ProjectId = _otherCompanyProjectId,
                GitHubRepositoryId = _otherCompanyRepositoryId,
                CompanyId = _otherCompanyId,
                LinkedByUserId = OutsiderUserId,
            });
        await seed.SaveChangesAsync();

        await using var db = NewContext();
        var links = await new UnitOfWork(db).ProjectRepositoryLinks
            .GetByCompany(_companyId)
            .ToListAsync();

        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Equal(_companyId, l.CompanyId));
    }

    [Fact]
    public async SystemTask.Task GetCompanyLinks_ReturnsEveryRepositoryWithTheProjectsItIsLinkedTo()
    {
        await using var seed = NewContext();

        // A second project so one repository can carry two chips — the case the old screen
        // could not render, and the reason this endpoint exists.
        var beta = new Project { Name = "Beta", Key = "BE", CompanyId = _companyId };
        seed.Projects.Add(beta);
        await seed.SaveChangesAsync();

        // Both orderings are seeded against their insertion order on purpose: this project's key
        // sorts first and its id is last, and the repository below does the same. Assert on rows
        // that arrive in insertion order and the assertions pass with the OrderBy clauses deleted.
        var aardvark = new Project { Name = "Aardvark", Key = "AA", CompanyId = _companyId };
        seed.Projects.Add(aardvark);

        var installationId = await seed.GitHubInstallations
            .Where(i => i.CompanyId == _companyId)
            .Select(i => i.Id)
            .SingleAsync();

        var earlyName = new GitHubRepository
        {
            RepositoryId = 5003,
            GitHubInstallationId = installationId,
            CompanyId = _companyId,
            FullName = "rigon-org/aardvark",
            DefaultBranch = "main",
        };
        seed.GitHubRepositories.Add(earlyName);
        await seed.SaveChangesAsync();

        seed.ProjectRepositoryLinks.AddRange(
            new ProjectRepositoryLink
            {
                ProjectId = _projectId,
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                LinkedByUserId = MemberUserId,
            },
            new ProjectRepositoryLink
            {
                ProjectId = beta.Id,
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                LinkedByUserId = MemberUserId,
            },
            new ProjectRepositoryLink
            {
                ProjectId = aardvark.Id,
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                LinkedByUserId = MemberUserId,
            });
        await seed.SaveChangesAsync();

        await using var db = NewContext();
        var result = await NewLinkService(db).GetCompanyLinksAsync(_companyId);

        Assert.True(result.IsSuccess);

        // Ordered by FullName, not by id: aardvark was inserted last and comes first.
        Assert.Equal(
            new[] { "rigon-org/aardvark", "rigon-org/alpha", "rigon-org/beta" },
            result.Value!.Repositories.Select(r => r.FullName));

        // Ordered by Key, not by id or by the order the links were created.
        var alpha = result.Value.Repositories.Single(r => r.FullName == "rigon-org/alpha");
        Assert.Equal(new[] { "AA", "AL", "BE" }, alpha.Projects.Select(p => p.Key));
        Assert.Equal("Alpha", alpha.Projects.Single(p => p.Key == "AL").Name);

        // A repository with no links is still a row — it is how you link the first project to it.
        var betaRepository = result.Value.Repositories.Single(r => r.FullName == "rigon-org/beta");
        Assert.Empty(betaRepository.Projects);

        Assert.Empty(result.Value.Unavailable);
    }


    [Fact]
    public async SystemTask.Task GetCompanyLinks_ReportsLinksWhoseRepositoryIsNoLongerLive()
    {
        await using var seed = NewContext();
        seed.ProjectRepositoryLinks.AddRange(
            new ProjectRepositoryLink
            {
                ProjectId = _projectId,
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                LinkedByUserId = MemberUserId,
            },
            new ProjectRepositoryLink
            {
                ProjectId = _projectId,
                GitHubRepositoryId = _secondRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = MemberUserId,
            });
        await seed.SaveChangesAsync();

        // The repository leaves the installation. The link row survives untouched.
        var dropped = await seed.GitHubRepositories.SingleAsync(r => r.Id == _secondRepositoryId);
        dropped.IsDeleted = true;
        dropped.DeletedAt = DateTime.UtcNow;
        await seed.SaveChangesAsync();

        await using var db = NewContext();
        var result = await NewLinkService(db).GetCompanyLinksAsync(_companyId);

        Assert.True(result.IsSuccess);

        // The dead repository is not a row, and its link is not silently gone either.
        Assert.Equal(new[] { "rigon-org/alpha" }, result.Value!.Repositories.Select(r => r.FullName));

        var unavailable = Assert.Single(result.Value.Unavailable);
        Assert.Equal(_projectId, unavailable.ProjectId);
        Assert.Equal("AL", unavailable.ProjectKey);
        Assert.Equal(1, unavailable.Count);

        // And the link row itself is still there, ready to come back with the repository.
        await using var verify = NewContext();
        Assert.Equal(2, await verify.ProjectRepositoryLinks.CountAsync());
    }

    [Fact]
    public async SystemTask.Task GetCompanyLinks_SkipsLinksWhoseProjectWasDeleted()
    {
        // Deleting a project does not cascade to ProjectRepositoryLinks, so this row is real.
        // It has no project to name and no chip to render, so it is skipped everywhere —
        // including the unavailable count. Accepted, documented, and out of scope to repair.
        await using var seed = NewContext();
        seed.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            ProjectId = _projectId,
            GitHubRepositoryId = _repositoryId,
            CompanyId = _companyId,
            LinkedByUserId = MemberUserId,
        });
        await seed.SaveChangesAsync();

        var project = await seed.Projects.SingleAsync(p => p.Id == _projectId);
        project.IsDeleted = true;
        project.DeletedAt = DateTime.UtcNow;
        await seed.SaveChangesAsync();

        await using var db = NewContext();
        var result = await NewLinkService(db).GetCompanyLinksAsync(_companyId);

        Assert.True(result.IsSuccess);
        // The count matters: "every row has no chips" is also true of no rows at all.
        Assert.Equal(2, result.Value!.Repositories.Count);
        Assert.All(result.Value.Repositories, r => Assert.Empty(r.Projects));
        Assert.Empty(result.Value.Unavailable);
    }

    [Fact]
    public async SystemTask.Task GetCompanyLinks_WithNoLinksAtAll_SucceedsWithEmptyProjectLists()
    {
        await using var db = NewContext();
        var result = await NewLinkService(db).GetCompanyLinksAsync(_companyId);

        // Success with empty chips, never a failure: "connected, nothing linked yet" is the
        // normal state of a fresh installation.
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Repositories.Count);
        Assert.All(result.Value.Repositories, r => Assert.Empty(r.Projects));
        Assert.Empty(result.Value.Unavailable);
    }

    [Fact]
    public async SystemTask.Task GetCompanyLinks_ForACompanyWithNoInstallation_ReturnsNothing()
    {
        await using var db = NewContext();
        var result = await NewLinkService(db).GetCompanyLinksAsync(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Repositories);
        Assert.Empty(result.Value.Unavailable);
    }

    [Fact]
    public async SystemTask.Task GetCompanyLinks_NeverLeaksAnotherCompanysRepositoriesOrLinks()
    {
        // Three reads compose this response and every one of them must be company-scoped. The
        // endpoint is admin-only, so a scoping slip here hands one tenant another's repository
        // names and project keys in a single call.
        await using var seed = NewContext();
        seed.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            ProjectId = _otherCompanyProjectId,
            GitHubRepositoryId = _otherCompanyRepositoryId,
            CompanyId = _otherCompanyId,
            LinkedByUserId = OutsiderUserId,
        });
        await seed.SaveChangesAsync();

        await using var db = NewContext();
        var result = await NewLinkService(db).GetCompanyLinksAsync(_companyId);

        Assert.True(result.IsSuccess);
        // Positive first: a read that returned nothing at all would satisfy every negative
        // assertion below it, and a broken companyId predicate is exactly how that happens.
        Assert.Equal(
            new[] { "rigon-org/alpha", "rigon-org/beta" },
            result.Value!.Repositories.Select(r => r.FullName));
        Assert.DoesNotContain(result.Value.Repositories, r => r.FullName == "foreign-org/secret");
        Assert.All(result.Value.Repositories, r => Assert.DoesNotContain(r.Projects, p => p.Key == "FR"));
        Assert.Empty(result.Value.Unavailable);
    }
}
