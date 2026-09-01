using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Services;
using TaskSphere.Application.Settings;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Registration is the one flow that reports a failed send to its caller: the account is kept and
/// the message says so. Rolling back would destroy a legitimately created company over a network
/// blip, and the rollback is itself multi-step.
/// </summary>
public class RegistrationVerificationTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereRegistrationVerificationTests;Trusted_Connection=True;TrustServerCertificate=True";

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
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private static RegisterDto Dto(string email) => new()
    {
        Name = "Acme", Email = email, Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
    };

    [Fact]
    public async SystemTask.Task Emails_a_verification_link_to_the_registered_address()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());

        var result = await harness.Accounts.RegisterAsync(Dto("newco@example.com"));

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(harness.Sender.Sent);
        Assert.Equal("newco@example.com", sent.To);
        Assert.Contains("/account/verify-email?", sent.Body);
    }

    [Fact]
    public async SystemTask.Task Leaves_the_new_account_unconfirmed()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new CapturingEmailSender());

        await harness.Accounts.RegisterAsync(Dto("unconfirmed@example.com"));

        Assert.False((await harness.Users.FindByEmailAsync("unconfirmed@example.com"))!.EmailConfirmed);
    }

    [Fact]
    public async SystemTask.Task Keeps_the_account_and_says_so_when_the_send_fails()
    {
        await using var db = NewContext();
        var harness = new RegistrationHarness(db, new FailingEmailSender());

        var result = await harness.Accounts.RegisterAsync(Dto("smtpdown@example.com"));

        Assert.True(result.IsSuccess);
        Assert.Contains("could not", result.Value, StringComparison.OrdinalIgnoreCase);
        // The company and the user both survive a dead mail server.
        Assert.NotNull(await harness.Users.FindByEmailAsync("smtpdown@example.com"));
        Assert.True(await db.Companies.AnyAsync(c => c.Name == "Acme"));
    }
}

/// <summary>
/// Builds AccountService and its collaborators over one context, the way the other integration
/// tests build services by hand.
/// </summary>
internal sealed class RegistrationHarness
{
    public Microsoft.AspNetCore.Identity.UserManager<TaskSphere.Domain.Entities.Identity.AppUser> Users { get; }
    public CapturingEmailSender Sender { get; } = null!;
    public AccountService Accounts { get; }

    public RegistrationHarness(ApplicationDbContext db, TaskSphere.Application.Interfaces.IEmailSender sender)
    {
        Users = IdentityTestHarness.NewUserManager(db);
        if (sender is CapturingEmailSender capturing) Sender = capturing;

        var roles = IdentityTestHarness.NewRoleManager(db);
        var unitOfWork = new TaskSphere.Infrastructure.Repositories.UnitOfWork(db);

        // Verified 2026-08-31: the profile lives in TaskSphere.Application.Mappings (plural), and
        // this one-argument MapperConfiguration is the idiom already used by
        // GitHubConnectTests.cs:124 and GitHubMappingAndErrorTests.cs:13.
        var mapper = new AutoMapper.MapperConfiguration(
            cfg => cfg.AddProfile<TaskSphere.Application.Mappings.MappingProfile>()).CreateMapper();

        // Verified 2026-08-31: CompanyService(IUnitOfWork, ILogger<CompanyService>, IMapper).
        var companies = new CompanyService(unitOfWork, NullLogger<CompanyService>.Instance, mapper);

        var verification = new AccountVerificationService(
            Users,
            sender,
            Options.Create(new ClientOptions { BaseUrl = "http://localhost:4200" }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AccountVerificationService>.Instance);

        Accounts = new AccountService(
            Users,
            IdentityTestHarness.NewSignInManager(Users),
            roles,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-key-that-is-long-enough-for-hmac-sha256-signing",
                ["Jwt:Issuer"] = "TaskSphereAPI",
                ["Jwt:Audience"] = "TaskSphereClient",
            }).Build(),
            NullLogger<AccountService>.Instance,
            companies,
            verification);
    }
}
