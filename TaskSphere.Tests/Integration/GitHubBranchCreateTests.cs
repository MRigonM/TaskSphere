using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Member = TaskSphere.Domain.Entities.Member;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Branch creation is the first GitHub write. Two projects, two repositories and two tasks,
/// deliberately: the routing and the authorization filter are both invisible to a fixture that
/// holds one of everything.
/// </summary>
public class GitHubBranchCreateTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereGitHubBranchCreateTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const string MemberUserId = "member-user";
    private const string OutsiderUserId = "outsider-user";

    private Guid _companyId;
    private int _projectId;        // TS, member is a Member, links _repositoryId only
    private int _otherProjectId;   // OTH, links _otherRepositoryId
    private int _repositoryId;
    private int _otherRepositoryId;
    private int _unlinkedRepositoryId;  // exists in the company, linked to no project
    private int _taskId;           // TS-42 "CRUD for Product"
    private int _otherTaskId;      // OTH-42, same number, different project

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

        var company = new Company { Name = "Branch Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        db.Users.AddRange(
            new AppUser { Id = MemberUserId, UserName = "member@x.io", Email = "member@x.io", Name = "Member", CompanyId = _companyId },
            new AppUser { Id = OutsiderUserId, UserName = "out@x.io", Email = "out@x.io", Name = "Outsider", CompanyId = _companyId });
        await db.SaveChangesAsync();

        var project = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        var otherProject = new Project { Name = "Other", Key = "OTH", CompanyId = _companyId };
        db.Projects.AddRange(project, otherProject);
        await db.SaveChangesAsync();
        _projectId = project.Id;
        _otherProjectId = otherProject.Id;

        db.Members.Add(new Member { ProjectId = _projectId, UserId = MemberUserId });
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 7401,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        // Seeded first and linked to nothing, for two reasons. It is the repository the
        // authorization test aims at — one that exists in this company but that the task's
        // project does not link. And it offsets the repository identity seed, so repository ids
        // and task ids can no longer coincide: with one of everything they were both 1 and 2,
        // and a lookup that used a task id where a repository id belongs resolved to the right
        // row by accident. Found by an independent mutation sweep on 2026-08-22.
        var unlinkedRepository = new GitHubRepository
        {
            RepositoryId = 557,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/secret",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(unlinkedRepository);
        await db.SaveChangesAsync();
        _unlinkedRepositoryId = unlinkedRepository.Id;

        var repository = new GitHubRepository
        {
            RepositoryId = 555,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var otherRepository = new GitHubRepository
        {
            RepositoryId = 556,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/web",
            DefaultBranch = "develop",
        };
        db.GitHubRepositories.AddRange(repository, otherRepository);
        await db.SaveChangesAsync();
        _repositoryId = repository.Id;
        _otherRepositoryId = otherRepository.Id;

        db.ProjectRepositoryLinks.AddRange(
            new ProjectRepositoryLink { CompanyId = _companyId, ProjectId = _projectId, GitHubRepositoryId = _repositoryId },
            new ProjectRepositoryLink { CompanyId = _companyId, ProjectId = _otherProjectId, GitHubRepositoryId = _otherRepositoryId });
        await db.SaveChangesAsync();

        var task = new TaskEntity { Title = "CRUD for Product", Number = 42, ProjectId = _projectId, CompanyId = _companyId };
        var otherTask = new TaskEntity { Title = "Other work", Number = 42, ProjectId = _otherProjectId, CompanyId = _companyId };
        db.Tasks.AddRange(task, otherTask);
        await db.SaveChangesAsync();
        _taskId = task.Id;
        _otherTaskId = otherTask.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private static GitHubBranchService NewService(ApplicationDbContext db, FakeGitHubApiClient api)
    {
        var unitOfWork = new UnitOfWork(db);

        return new GitHubBranchService(
            unitOfWork,
            new AccessControlService(db),
            api,
            new GitHubTaskLinkResolver(unitOfWork));
    }

    [Fact]
    public async SystemTask.Task Suggestion_ProposesTheKeyedName_AndTheLinkedRepositoryOnly()
    {
        await using var db = NewContext();
        var result = await NewService(db, new FakeGitHubApiClient())
            .GetSuggestionAsync(_companyId, MemberUserId, isCompanyAdmin: false, _taskId, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("TS-42", result.Value!.TaskKey);
        Assert.Equal("TS-42/crud-for-product", result.Value.SuggestedName);
        var option = Assert.Single(result.Value.Repositories);
        Assert.Equal("rigon-org/api", option.FullName);
        Assert.Equal("main", option.DefaultBranch);
    }

    [Fact]
    public async SystemTask.Task Suggestion_ForANonMember_IsForbidden()
    {
        await using var db = NewContext();
        var result = await NewService(db, new FakeGitHubApiClient())
            .GetSuggestionAsync(_companyId, OutsiderUserId, isCompanyAdmin: false, _taskId, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
    }

    [Fact]
    public async SystemTask.Task Suggestion_ForACompanyAdmin_SkipsTheMembershipCheck()
    {
        await using var db = NewContext();
        var result = await NewService(db, new FakeGitHubApiClient())
            .GetSuggestionAsync(_companyId, OutsiderUserId, isCompanyAdmin: true, _taskId, default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async SystemTask.Task Suggestion_ForAProjectWithNoLink_SaysSo()
    {
        await using (var arrange = NewContext())
        {
            var link = await arrange.ProjectRepositoryLinks.FirstAsync(l => l.ProjectId == _projectId);
            arrange.ProjectRepositoryLinks.Remove(link);
            await arrange.SaveChangesAsync();
        }

        await using var db = NewContext();
        var result = await NewService(db, new FakeGitHubApiClient())
            .GetSuggestionAsync(_companyId, MemberUserId, isCompanyAdmin: false, _taskId, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.NoLinkedRepository", result.Errors[0].Code);
    }

    private const string RefBody = "{\"ref\":\"refs/heads/main\",\"object\":{\"sha\":\"basesha123\"}}";

    [Fact]
    public async SystemTask.Task Create_PostsTheRefAndTheBaseSha_ThenMirrorsAndLinksTheBranch()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .OnPost("git/refs", "{\"ref\":\"refs/heads/TS-42/crud-for-product\"}");

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyExisted);
        Assert.Equal("TS-42/crud-for-product", result.Value.Name);
        Assert.Equal("basesha123", result.Value.HeadSha);
        Assert.Equal("https://github.com/rigon-org/api/tree/TS-42/crud-for-product", result.Value.HtmlUrl);

        // The body, not just the URL: a create that posted the wrong ref or the wrong base
        // would pass a URL-only assertion.
        var (url, body) = Assert.Single(api.Posts);
        Assert.Equal("https://api.github.com/repos/rigon-org/api/git/refs", url);
        Assert.Contains("\"ref\":\"refs/heads/TS-42/crud-for-product\"", body);
        Assert.Contains("\"sha\":\"basesha123\"", body);

        await using var verify = NewContext();
        var branch = await verify.GitHubBranches.SingleAsync(b => b.GitHubRepositoryId == _repositoryId);
        Assert.Equal("TS-42/crud-for-product", branch.Name);
        Assert.Equal("basesha123", branch.HeadSha);
        Assert.False(branch.IsDeleted);

        // The resolver ran over the mirror, so the branch is already on the task.
        var link = await verify.TaskLinks.SingleAsync();
        Assert.Equal(_taskId, link.TaskId);
        Assert.Equal(branch.Id, link.GitHubBranchId);
    }

    [Fact]
    public async SystemTask.Task Create_ReadsTheBaseFromTheRepositorysOwnDefaultBranch()
    {
        // The other project's repository defaults to "develop", not "main". A hardcoded
        // "main" would pass every single-repository test and fail here.
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/develop", "{\"object\":{\"sha\":\"devsha\"}}")
            .OnPost("git/refs", "{}");

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, OutsiderUserId, isCompanyAdmin: true, _otherTaskId,
            new CreateBranchDto(null, "OTH-42/other-work"), default);

        Assert.True(result.IsSuccess);
        Assert.Contains(api.GetUrls, u => u.EndsWith("/git/ref/heads/develop", StringComparison.Ordinal));
        Assert.Equal("devsha", result.Value!.HeadSha);
    }

    [Fact]
    public async SystemTask.Task Create_LinksTheBranchToTheRightTask_WhenTwoProjectsShareATaskNumber()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/develop", "{\"object\":{\"sha\":\"devsha\"}}")
            .OnPost("git/refs", "{}");

        await using var db = NewContext();
        await NewService(db, api).CreateForTaskAsync(
            _companyId, OutsiderUserId, isCompanyAdmin: true, _otherTaskId,
            new CreateBranchDto(null, "OTH-42/other-work"), default);

        await using var verify = NewContext();
        var link = await verify.TaskLinks.SingleAsync();
        Assert.Equal(_otherTaskId, link.TaskId);
    }

    // Task 7: The refusals — wrong repository, bad name, another task's key

    /// <summary>
    /// TS links rigon-org/api only. A request naming the other repository must not put the
    /// branch there: with one link the request's repositoryId is ignored, not obeyed.
    /// </summary>
    [Fact]
    public async SystemTask.Task Create_WithOneLink_IgnoresARequestedRepository_RatherThanTrustingIt()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .OnPost("git/refs", "{}");

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(_otherRepositoryId, "TS-42/crud"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://api.github.com/repos/rigon-org/api/git/refs", Assert.Single(api.Posts).Url);

        await using var verify = NewContext();
        Assert.Equal(_repositoryId, (await verify.GitHubBranches.SingleAsync()).GitHubRepositoryId);
    }

    /// <summary>
    /// With several links the choice cannot be guessed: picking the first would put the branch
    /// in a repository nobody named.
    /// </summary>
    [Fact]
    public async SystemTask.Task Create_WithSeveralLinks_RequiresAChoice()
    {
        await using (var arrange = NewContext())
        {
            arrange.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                CompanyId = _companyId,
                ProjectId = _projectId,
                GitHubRepositoryId = _otherRepositoryId,
            });
            await arrange.SaveChangesAsync();
        }

        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.RepositoryRequired", result.Errors[0].Code);
        Assert.Empty(api.Posts);
    }

    /// <summary>
    /// A refusal that still spent a GitHub call would leak the existence of the task to a
    /// non-member through rate limit and through the audit trail. Nothing is called at all.
    /// </summary>
    [Fact]
    public async SystemTask.Task Create_RefusedForANonMember_NeverCallsGitHub()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, OutsiderUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
        Assert.Empty(api.Posts);
        Assert.Empty(api.GetUrls);
    }

    [Fact]
    public async SystemTask.Task Create_WithSeveralLinks_RefusesARepositoryThisProjectDoesNotLink()
    {
        await using (var arrange = NewContext())
        {
            arrange.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                CompanyId = _companyId,
                ProjectId = _projectId,
                GitHubRepositoryId = _otherRepositoryId,
            });
            await arrange.SaveChangesAsync();
        }

        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(_unlinkedRepositoryId, "TS-42/crud"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
        Assert.Empty(api.Posts);
    }

    [Fact]
    public async SystemTask.Task Create_RefusesAnIllegalRefName()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/has..dots"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation.BranchName", result.Errors[0].Code);
        // The two validation branches share an error code, so only the message distinguishes
        // them. Without this, swapping the order of the checks changes nothing observable and
        // an illegal name gets told to add a key it already has.
        Assert.Contains("valid git branch name", result.Errors[0].Description);
        Assert.Empty(api.Posts);
    }

    [Fact]
    public async SystemTask.Task Create_RefusesANameThatDoesNotNameThisTask()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-7/other"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation.BranchName", result.Errors[0].Code);
        Assert.Empty(api.Posts);
        Assert.Empty(api.GetUrls);
    }

    // Task 8: GitHub's own failures — already exists, not approved, missing base

    [Fact]
    public async SystemTask.Task Create_WhenTheRefAlreadyExists_IsSuccess_WithTheBranchsOwnHead()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .OnGet("git/ref/heads/TS-42/crud-for-product", "{\"object\":{\"sha\":\"branchsha\"}}")
            .FailPost("git/refs", new Error(
                "GitHub.UnprocessableEntity",
                "GitHub returned 422 for https://api.github.com/repos/rigon-org/api/git/refs. Reference already exists"));

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyExisted);
        // Not the base sha: the branch is wherever GitHub has it.
        Assert.Equal("branchsha", result.Value.HeadSha);

        await using var verify = NewContext();
        Assert.Equal("branchsha", (await verify.GitHubBranches.SingleAsync()).HeadSha);
    }

    [Fact]
    public async SystemTask.Task Create_WhenWriteAccessIsNotApproved_SaysWhoHasToApproveIt()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .FailPost("git/refs", new Error("GitHub.Forbidden", "GitHub returned 403 for .../git/refs. Resource not accessible by integration"));

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.WriteNotApproved", result.Errors[0].Code);
        Assert.Contains("approve", result.Errors[0].Description, StringComparison.OrdinalIgnoreCase);

        await using var verify = NewContext();
        Assert.Empty(await verify.GitHubBranches.ToListAsync());
    }

    [Fact]
    public async SystemTask.Task Create_WhenTheDefaultBranchIsGone_NamesTheDefaultBranch_NotTheNewOne()
    {
        var api = new FakeGitHubApiClient()
            .FailGet("git/ref/heads/main", new Error("GitHub.NotFound", "GitHub returned 404 for .../git/ref/heads/main."));

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.DefaultBranchMissing", result.Errors[0].Code);
        // Both halves, and in their own slots: asserting only that "main" appears somewhere
        // passes just as well when the repository and the branch are swapped in the message.
        Assert.Contains("no branch 'main'", result.Errors[0].Description);
        Assert.Contains("in rigon-org/api", result.Errors[0].Description);
        Assert.Empty(api.Posts);
    }

    [Fact]
    public async SystemTask.Task Create_WhenRateLimited_PassesTheTypedFailureThrough()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .FailPost("git/refs", new Error("GitHub.RateLimited", "GitHub rate limit hit. Retry after 60s."));

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.RateLimited", result.Errors[0].Code);
    }

    // Task 9: Reviving a soft-deleted branch row

    [Fact]
    public async SystemTask.Task Create_RevivesASoftDeletedRow_RatherThanCollidingWithTheIndex()
    {
        await using (var arrange = NewContext())
        {
            arrange.GitHubBranches.Add(new GitHubBranch
            {
                GitHubRepositoryId = _repositoryId,
                CompanyId = _companyId,
                Name = "TS-42/crud-for-product",
                HeadSha = "oldsha",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow.AddDays(-3),
            });
            await arrange.SaveChangesAsync();
        }

        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .OnPost("git/refs", "{}");

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.True(result.IsSuccess);

        await using var verify = NewContext();
        var rows = await verify.GitHubBranches.IgnoreQueryFilters()
            .Where(b => b.GitHubRepositoryId == _repositoryId)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAt);
        Assert.Equal("basesha123", row.HeadSha);
    }

    [Fact]
    public async SystemTask.Task Create_Twice_KeepsOneRowAndOneLink()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .OnPost("git/refs", "{}");

        await using (var first = NewContext())
        {
            await NewService(first, api).CreateForTaskAsync(
                _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
                new CreateBranchDto(null, "TS-42/crud-for-product"), default);
        }

        await using (var second = NewContext())
        {
            var result = await NewService(second, api).CreateForTaskAsync(
                _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
                new CreateBranchDto(null, "TS-42/crud-for-product"), default);

            Assert.True(result.IsSuccess);
        }

        await using var verify = NewContext();
        Assert.Single(await verify.GitHubBranches.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await verify.TaskLinks.ToListAsync());
    }

    // The tests below close gaps found by an independent mutation sweep on 2026-08-22. Each one
    // exists because a mutant survived everything above it.

    /// <summary>
    /// Which installation a call carries decides whose GitHub account it lands in, so a wrong
    /// one is a tenancy leak rather than a bug in a URL. Nothing in this file could see it until
    /// the fake started recording the argument.
    /// </summary>
    [Fact]
    public async SystemTask.Task Create_CallsGitHubAsThisCompanysOwnInstallation()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .OnPost("git/refs", "{}");

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(api.InstallationIds);
        Assert.All(api.InstallationIds, id => Assert.Equal(7401L, id));
    }

    /// <summary>
    /// The membership gate runs before the task lookup, so a non-member cannot tell a task that
    /// does not exist from one they may not see. Pairing the two is the only way to observe the
    /// order: either check alone answers the same way.
    /// </summary>
    [Fact]
    public async SystemTask.Task Create_ForANonMember_OnANonexistentTask_IsForbidden_NotNotFound()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, OutsiderUserId, isCompanyAdmin: false, taskId: 999_999,
            new CreateBranchDto(null, "TS-42/crud"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
    }

    [Fact]
    public async SystemTask.Task Create_ForATaskWithNoProject_SaysSo()
    {
        int looseTaskId;

        await using (var arrange = NewContext())
        {
            var loose = new TaskEntity { Title = "No project", Number = 1, ProjectId = null, CompanyId = _companyId };
            arrange.Tasks.Add(loose);
            await arrange.SaveChangesAsync();
            looseTaskId = loose.Id;
        }

        await using var db = NewContext();
        var result = await NewService(db, new FakeGitHubApiClient()).CreateForTaskAsync(
            _companyId, OutsiderUserId, isCompanyAdmin: true, looseTaskId,
            new CreateBranchDto(null, "TS-42/crud"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.TaskHasNoProject", result.Errors[0].Code);
    }

    [Fact]
    public async SystemTask.Task Create_WhenTheCompanyIsNotConnectedToGitHub_SaysSo()
    {
        await using (var arrange = NewContext())
        {
            // Soft-deleted rather than removed, which is both what a disconnect actually does
            // and the only option here: the repositories carry a foreign key to it.
            var installation = await arrange.GitHubInstallations.SingleAsync();
            installation.IsDeleted = true;
            installation.DeletedAt = DateTime.UtcNow;
            await arrange.SaveChangesAsync();
        }

        await using var db = NewContext();
        var result = await NewService(db, new FakeGitHubApiClient()).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("GitHub.NotConnected", result.Errors[0].Code);
    }

    /// <summary>
    /// A name pasted with surrounding whitespace is a legal intent, not a rejection: a leading
    /// space would otherwise fail the ref allowlist and read as "that is not a valid name".
    /// </summary>
    [Fact]
    public async SystemTask.Task Create_TrimsWhitespaceAroundTheName_RatherThanRefusingIt()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .OnPost("git/refs", "{}");

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "  TS-42/crud-for-product  "), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("TS-42/crud-for-product", result.Value!.Name);
        Assert.Contains("\"ref\":\"refs/heads/TS-42/crud-for-product\"", Assert.Single(api.Posts).Body);
    }

    /// <summary>
    /// When the ref already exists, its head is read from the branch itself — and if that read
    /// fails, the base SHA stands rather than a null being written into a non-null column. The
    /// failure path of the second read had no test at all.
    /// </summary>
    [Fact]
    public async SystemTask.Task Create_WhenTheExistingRefsHeadCannotBeRead_KeepsTheBaseSha()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .FailGet("git/ref/heads/TS-42/crud-for-product", new Error("GitHub.RequestFailed", "GitHub returned 500."))
            .FailPost("git/refs", new Error(
                "GitHub.UnprocessableEntity",
                "GitHub returned 422 for .../git/refs. Reference already exists"));

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyExisted);
        Assert.Equal("basesha123", result.Value.HeadSha);

        await using var verify = NewContext();
        Assert.Equal("basesha123", (await verify.GitHubBranches.SingleAsync()).HeadSha);
    }

    /// <summary>
    /// The already-exists match reads GitHub's prose, which is not ours to spell. Matching it
    /// case-sensitively would turn a branch that exists into a failure on a wording change.
    /// </summary>
    [Fact]
    public async SystemTask.Task Create_RecognisesTheAlreadyExistsMessage_WhateverItsCasing()
    {
        var api = new FakeGitHubApiClient()
            .OnGet("git/ref/heads/main", RefBody)
            .OnGet("git/ref/heads/TS-42/crud-for-product", "{\"object\":{\"sha\":\"branchsha\"}}")
            .FailPost("git/refs", new Error(
                "GitHub.UnprocessableEntity",
                "GitHub returned 422 for .../git/refs. reference already EXISTS"));

        await using var db = NewContext();
        var result = await NewService(db, api).CreateForTaskAsync(
            _companyId, MemberUserId, isCompanyAdmin: false, _taskId,
            new CreateBranchDto(null, "TS-42/crud-for-product"), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyExisted);
    }

    /// <summary>
    /// The candidate repositories are what the dialog's picker renders, so their order is part
    /// of the contract rather than an accident of the query.
    /// </summary>
    [Fact]
    public async SystemTask.Task Suggestion_OrdersTheCandidateRepositoriesByName()
    {
        await using (var arrange = NewContext())
        {
            arrange.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
            {
                CompanyId = _companyId,
                ProjectId = _projectId,
                GitHubRepositoryId = _otherRepositoryId,
            });
            await arrange.SaveChangesAsync();
        }

        await using var db = NewContext();
        var result = await NewService(db, new FakeGitHubApiClient())
            .GetSuggestionAsync(_companyId, MemberUserId, isCompanyAdmin: false, _taskId, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { "rigon-org/api", "rigon-org/web" },
            result.Value!.Repositories.Select(r => r.FullName).ToArray());
    }
}

/// <summary>
/// Queued per URL fragment so a test states only the calls it cares about. Requests are
/// recorded — a create must be asserted on its *body*, not only on its URL.
/// </summary>
internal sealed class FakeGitHubApiClient : TaskSphere.Application.Interfaces.IGitHubApiClient
{
    private readonly Dictionary<string, Result<TaskSphere.Application.Interfaces.GitHubResponse>> _gets = new();
    private readonly Dictionary<string, Result<TaskSphere.Application.Interfaces.GitHubResponse>> _posts = new();

    public List<string> GetUrls { get; } = new();
    public List<(string Url, string Body)> Posts { get; } = new();

    /// <summary>
    /// Every installation id this client was called with. Recorded because the id decides which
    /// company's GitHub account a write lands in: a call carrying the wrong one is a tenancy
    /// leak, and until this list existed nothing in the file could see it.
    /// </summary>
    public List<long> InstallationIds { get; } = new();

    public FakeGitHubApiClient OnGet(string fragment, string body)
    {
        _gets[fragment] = Result<TaskSphere.Application.Interfaces.GitHubResponse>.Success(
            new TaskSphere.Application.Interfaces.GitHubResponse(body, null));
        return this;
    }

    public FakeGitHubApiClient FailGet(string fragment, Error error)
    {
        _gets[fragment] = Result<TaskSphere.Application.Interfaces.GitHubResponse>.Failure(error);
        return this;
    }

    public FakeGitHubApiClient OnPost(string fragment, string body)
    {
        _posts[fragment] = Result<TaskSphere.Application.Interfaces.GitHubResponse>.Success(
            new TaskSphere.Application.Interfaces.GitHubResponse(body, null));
        return this;
    }

    public FakeGitHubApiClient FailPost(string fragment, Error error)
    {
        _posts[fragment] = Result<TaskSphere.Application.Interfaces.GitHubResponse>.Failure(error);
        return this;
    }

    public SystemTask.Task<Result<TaskSphere.Application.Interfaces.GitHubResponse>> GetAsync(
        long installationId, string url, CancellationToken cancellationToken = default)
    {
        GetUrls.Add(url);
        InstallationIds.Add(installationId);
        return SystemTask.Task.FromResult(Match(_gets, url));
    }

    public SystemTask.Task<Result<TaskSphere.Application.Interfaces.GitHubResponse>> PostAsync(
        long installationId, string url, string jsonBody, CancellationToken cancellationToken = default)
    {
        Posts.Add((url, jsonBody));
        InstallationIds.Add(installationId);
        return SystemTask.Task.FromResult(Match(_posts, url));
    }

    private static Result<TaskSphere.Application.Interfaces.GitHubResponse> Match(
        Dictionary<string, Result<TaskSphere.Application.Interfaces.GitHubResponse>> queued, string url)
    {
        foreach (var (fragment, result) in queued)
        {
            if (url.Contains(fragment, StringComparison.Ordinal))
                return result;
        }

        return Result<TaskSphere.Application.Interfaces.GitHubResponse>.Failure(
            new Error("Test.NoStub", $"No stubbed response for {url}."));
    }
}
