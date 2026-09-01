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

public class AccountRecoveryTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereAccountRecoveryTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AccountVerificationService NewService(
        UserManager<AppUser> users, IEmailSender sender) =>
        new(
            users,
            sender,
            Options.Create(new ClientOptions { BaseUrl = "http://localhost:4200" }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AccountVerificationService>.Instance);

    /// A member as the invitation flow creates them: no password, unconfirmed address.
    private static async Task<AppUser> NewInvitedUser(UserManager<AppUser> users, string email)
    {
        var user = new AppUser { UserName = email, Email = email, Name = "Member" };
        var created = await users.CreateAsync(user);
        Assert.True(created.Succeeded);
        return user;
    }

    private static async Task<AppUser> NewUserWithPassword(UserManager<AppUser> users, string email)
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
    public async SystemTask.Task An_invited_member_sets_a_password_and_can_then_sign_in()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new CapturingEmailSender());
        var user = await NewInvitedUser(users, "invited@example.com");
        Assert.False(await users.HasPasswordAsync(user));

        var token = AccountEmails.EncodeToken(await users.GeneratePasswordResetTokenAsync(user));
        var result = await service.AcceptInviteAsync(new AcceptInviteDto
        {
            Email = "invited@example.com", Token = token,
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        });

        Assert.True(result.IsSuccess);
        var signIn = IdentityTestHarness.NewSignInManager(users);
        var reloaded = await users.FindByEmailAsync("invited@example.com");
        Assert.True((await signIn.CheckPasswordSignInAsync(reloaded!, "Str0ng!Password", false)).Succeeded);
    }

    [Fact]
    public async SystemTask.Task Accepting_an_invitation_confirms_the_address()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new CapturingEmailSender());
        var user = await NewInvitedUser(users, "confirmed-by-invite@example.com");

        var token = AccountEmails.EncodeToken(await users.GeneratePasswordResetTokenAsync(user));
        await service.AcceptInviteAsync(new AcceptInviteDto
        {
            Email = "confirmed-by-invite@example.com", Token = token,
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        });

        // The rule the whole design rests on: setting a password through a link sent to the
        // address IS the confirmation. Asserted against the database, not the return value.
        await using var check = NewContext();
        var stored = await check.Users.SingleAsync(u => u.Email == "confirmed-by-invite@example.com");
        Assert.True(stored.EmailConfirmed);
    }

    [Fact]
    public async SystemTask.Task A_reset_confirms_an_address_that_was_never_verified()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new CapturingEmailSender());
        var user = await NewUserWithPassword(users, "never-verified@example.com");
        Assert.False(user.EmailConfirmed);

        var token = AccountEmails.EncodeToken(await users.GeneratePasswordResetTokenAsync(user));
        var result = await service.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = "never-verified@example.com", Token = token,
            Password = "N3w!Password", ConfirmPassword = "N3w!Password",
        });

        Assert.True(result.IsSuccess);
        // Without this, a user who never verified, forgot their password and reset it would
        // still be refused at login by a gate their reset had already satisfied.
        await using var check = NewContext();
        var stored = await check.Users.SingleAsync(u => u.Email == "never-verified@example.com");
        Assert.True(stored.EmailConfirmed);
    }

    [Fact]
    public async SystemTask.Task A_reset_actually_changes_the_password()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new CapturingEmailSender());
        var user = await NewUserWithPassword(users, "changed@example.com");

        var token = AccountEmails.EncodeToken(await users.GeneratePasswordResetTokenAsync(user));
        await service.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = "changed@example.com", Token = token,
            Password = "N3w!Password", ConfirmPassword = "N3w!Password",
        });

        var signIn = IdentityTestHarness.NewSignInManager(users);
        var reloaded = await users.FindByEmailAsync("changed@example.com");
        Assert.True((await signIn.CheckPasswordSignInAsync(reloaded!, "N3w!Password", false)).Succeeded);
        Assert.False((await signIn.CheckPasswordSignInAsync(reloaded!, "Str0ng!Password", false)).Succeeded);
    }

    [Fact]
    public async SystemTask.Task A_second_use_of_the_same_invitation_link_is_refused()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new CapturingEmailSender());
        var user = await NewInvitedUser(users, "reused@example.com");
        var token = AccountEmails.EncodeToken(await users.GeneratePasswordResetTokenAsync(user));

        var dto = new AcceptInviteDto
        {
            Email = "reused@example.com", Token = token,
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        };
        await service.AcceptInviteAsync(dto);

        // Setting the password rolls the security stamp, which is what invalidates the token.
        // Unlike a verification link, a re-used invite must NOT read as success: it would let
        // anyone holding an old link set the password again.
        var second = await service.AcceptInviteAsync(dto);

        Assert.False(second.IsSuccess);
        Assert.Contains(second.Errors, e => e.Code == "Auth.TokenInvalid");
    }

    [Fact]
    public async SystemTask.Task A_tampered_token_is_refused_without_throwing()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new CapturingEmailSender());
        await NewInvitedUser(users, "tampered-invite@example.com");

        var result = await service.AcceptInviteAsync(new AcceptInviteDto
        {
            Email = "tampered-invite@example.com", Token = "not a token!!",
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == "Auth.TokenInvalid");
    }

    [Fact]
    public async SystemTask.Task An_unknown_address_gets_the_same_answer_as_a_bad_token()
    {
        await using var db = NewContext();
        var users = IdentityTestHarness.NewUserManager(db);
        var service = NewService(users, new CapturingEmailSender());

        var result = await service.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = "nobody@example.com", Token = "whatever",
            Password = "Str0ng!Password", ConfirmPassword = "Str0ng!Password",
        });

        // A distinct "no such account" here would make this endpoint an oracle just like the
        // ones the neutral answers protect.
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == "Auth.TokenInvalid");
    }
}
