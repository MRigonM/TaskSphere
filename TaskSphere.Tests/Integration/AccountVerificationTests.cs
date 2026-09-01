using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Services;
using TaskSphere.Application.Settings;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Infrastructure.Data;

using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Captures what was sent, so a test can assert the address and read the link out of the body.
/// </summary>
internal sealed class CapturingEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task<Result> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        Sent.Add((to, subject, htmlBody));
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// A sender that always fails, for the paths whose whole point is surviving a failed send.
/// </summary>
internal sealed class FailingEmailSender : IEmailSender
{
    public Task<Result> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => Task.FromResult(Result.Failure(new Error("Email.SendFailed", "The email could not be sent.")));
}

public class AccountVerificationTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereAccountVerificationTests;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static async Task<AppUser> NewUnconfirmedUser(UserManager<AppUser> users, string email)
    {
        var user = new AppUser { UserName = email, Email = email, Name = "Test" };
        await users.CreateAsync(user, "Str0ng!Password");
        return user;
    }

    /// Pulls the token out of the link the service put in the email body.
    private static string TokenFromLink(string body)
    {
        var start = body.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var end = body.IndexOfAny(new[] { '"', '<', '\n', ' ' }, start);
        return body[start..(end < 0 ? body.Length : end)];
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task Confirms_an_address_from_the_token_in_the_email()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);
        await NewUnconfirmedUser(users, "confirm@example.com");

        await service.ResendVerificationAsync(new EmailOnlyDto { Email = "confirm@example.com" });
        var token = TokenFromLink(sender.Sent.Single().Body);

        var result = await service.VerifyEmailAsync(
            new VerifyEmailDto { Email = "confirm@example.com", Token = token });

        Assert.True(result.IsSuccess);
        Assert.True((await users.FindByEmailAsync("confirm@example.com"))!.EmailConfirmed);
    }

    [Fact]
    public async SystemTask.Task Treats_a_second_click_on_the_same_link_as_success()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);
        await NewUnconfirmedUser(users, "twice@example.com");

        await service.ResendVerificationAsync(new EmailOnlyDto { Email = "twice@example.com" });
        var token = TokenFromLink(sender.Sent.Single().Body);
        await service.VerifyEmailAsync(new VerifyEmailDto { Email = "twice@example.com", Token = token });

        // A double-clicked link, or a mail client that prefetches it, must not read as an error.
        var second = await service.VerifyEmailAsync(
            new VerifyEmailDto { Email = "twice@example.com", Token = token });

        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async SystemTask.Task Rejects_a_tampered_token_without_throwing()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new CapturingEmailSender());
        await NewUnconfirmedUser(users, "tampered@example.com");

        var result = await service.VerifyEmailAsync(
            new VerifyEmailDto { Email = "tampered@example.com", Token = "not a token!!" });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == "Auth.TokenInvalid");
        Assert.False((await users.FindByEmailAsync("tampered@example.com"))!.EmailConfirmed);
    }

    [Fact]
    public async SystemTask.Task Answers_the_same_way_for_an_address_that_has_no_account()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);

        var result = await service.ResendVerificationAsync(new EmailOnlyDto { Email = "nobody@example.com" });

        // Neutral by design: this endpoint is anonymous, and a different answer here turns it
        // into an oracle for which addresses are registered.
        Assert.True(result.IsSuccess);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async SystemTask.Task Sends_nothing_for_an_address_that_is_already_confirmed()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);
        var user = await NewUnconfirmedUser(users, "already@example.com");
        await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));

        var result = await service.ResendVerificationAsync(new EmailOnlyDto { Email = "already@example.com" });

        Assert.True(result.IsSuccess);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async SystemTask.Task Sends_once_when_asked_twice_inside_the_cooldown()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var sender = new CapturingEmailSender();
        var service = NewService(users, sender);
        await NewUnconfirmedUser(users, "cooldown@example.com");

        var first = await service.ResendVerificationAsync(new EmailOnlyDto { Email = "cooldown@example.com" });
        var second = await service.ResendVerificationAsync(new EmailOnlyDto { Email = "cooldown@example.com" });

        Assert.Single(sender.Sent);
        // Both answers are identical, which is what makes the skip invisible.
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public async SystemTask.Task Reports_success_even_when_the_send_fails()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new FailingEmailSender());
        await NewUnconfirmedUser(users, "smtpdown@example.com");

        var result = await service.ResendVerificationAsync(new EmailOnlyDto { Email = "smtpdown@example.com" });

        // Saying "we could not send it" here would leak that the address exists.
        Assert.True(result.IsSuccess);
    }
}
