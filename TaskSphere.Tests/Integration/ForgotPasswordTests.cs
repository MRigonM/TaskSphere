using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Services;
using TaskSphere.Application.Settings;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class ForgotPasswordTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereForgotPasswordTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AccountVerificationService NewService(
        UserManager<AppUser> users, IEmailSender sender, IMemoryCache? cache = null) =>
        new(
            users,
            sender,
            Options.Create(new ClientOptions { BaseUrl = "http://localhost:4200" }),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AccountVerificationService>.Instance);

    private static async Task<AppUser> NewUser(UserManager<AppUser> users, string email)
    {
        var user = new AppUser { UserName = email, Email = email, Name = "Test" };
        await users.CreateAsync(user, "Str0ng!Password");
        return user;
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task Emails_a_reset_link_to_an_address_that_has_an_account()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);
        await NewUser(users, "forgot@example.com");

        var result = await service.ForgotPasswordAsync(new EmailOnlyDto { Email = "forgot@example.com" });

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(sender.Sent);
        Assert.Equal("forgot@example.com", sent.To);
        Assert.Contains("/account/reset-password?", sent.Body);
    }

    [Fact]
    public async SystemTask.Task Answers_the_same_way_and_sends_nothing_for_an_unknown_address()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);
        await NewUser(users, "known@example.com");

        var known = await service.ForgotPasswordAsync(new EmailOnlyDto { Email = "known@example.com" });
        var unknown = await service.ForgotPasswordAsync(new EmailOnlyDto { Email = "nobody@example.com" });

        // Identical answers are the whole point: any difference turns this anonymous endpoint
        // into an oracle for which addresses are registered.
        Assert.True(unknown.IsSuccess);
        Assert.Equal(known.Value, unknown.Value);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async SystemTask.Task Reports_success_even_when_the_send_fails()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new FailingEmailSender());
        await NewUser(users, "smtpdown-forgot@example.com");

        var result = await service.ForgotPasswordAsync(
            new EmailOnlyDto { Email = "smtpdown-forgot@example.com" });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async SystemTask.Task Sends_once_when_asked_twice_inside_the_cooldown()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);
        await NewUser(users, "throttled@example.com");

        var first = await service.ForgotPasswordAsync(new EmailOnlyDto { Email = "throttled@example.com" });
        var second = await service.ForgotPasswordAsync(new EmailOnlyDto { Email = "throttled@example.com" });

        Assert.Single(sender.Sent);
        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public async SystemTask.Task A_resend_does_not_throttle_a_forgot_password()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);
        await NewUser(users, "twoflows@example.com");

        // Both flows are anonymous, both send mail, both answer neutrally — so sharing one
        // cooldown bucket would let a resend silently swallow a password-reset request, and the
        // neutral answer would hide it.
        await service.ResendVerificationAsync(new EmailOnlyDto { Email = "twoflows@example.com" });
        await service.ForgotPasswordAsync(new EmailOnlyDto { Email = "twoflows@example.com" });

        Assert.Equal(2, sender.Sent.Count);
        Assert.Contains(sender.Sent, s => s.Body.Contains("/account/verify-email?"));
        Assert.Contains(sender.Sent, s => s.Body.Contains("/account/reset-password?"));
    }
}
