using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// Builds a real UserManager and SignInManager over a real database, because this project has no
/// HTTP host harness and Identity is not otherwise reachable from a test. Password rules mirror
/// ApplicationServices.cs so a password accepted here is accepted by the running app.
/// </summary>
internal static class IdentityTestHarness
{
    public static UserManager<AppUser> NewUserManager(ApplicationDbContext db)
    {
        var options = Options.Create(new IdentityOptions());
        options.Value.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultProvider;
        options.Value.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
        options.Value.Password.RequiredLength = 8;
        options.Value.Password.RequireUppercase = true;
        options.Value.Password.RequireDigit = true;
        options.Value.Password.RequireNonAlphanumeric = true;
        options.Value.Password.RequireLowercase = true;

        var services = new ServiceCollection().AddLogging().AddDataProtection().Services.BuildServiceProvider();

        var manager = new UserManager<AppUser>(
            new UserStore<AppUser>(db),
            options,
            new PasswordHasher<AppUser>(),
            new IUserValidator<AppUser>[] { new UserValidator<AppUser>() },
            new IPasswordValidator<AppUser>[] { new PasswordValidator<AppUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            NullLogger<UserManager<AppUser>>.Instance);

        // The DI container registers this through AddDefaultTokenProviders(); a hand-built
        // UserManager has no providers at all, and GenerateEmailConfirmationTokenAsync throws
        // NotSupportedException without one.
        manager.RegisterTokenProvider(
            TokenOptions.DefaultProvider,
            new DataProtectorTokenProvider<AppUser>(
                services.GetRequiredService<IDataProtectionProvider>(),
                Options.Create(new DataProtectionTokenProviderOptions()),
                NullLogger<DataProtectorTokenProvider<AppUser>>.Instance));

        return manager;
    }

    public static RoleManager<IdentityRole> NewRoleManager(ApplicationDbContext db) =>
        new(
            new RoleStore<IdentityRole>(db),
            new IRoleValidator<IdentityRole>[] { new RoleValidator<IdentityRole>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);

    public static SignInManager<AppUser> NewSignInManager(UserManager<AppUser> users)
    {
        var options = Options.Create(new IdentityOptions());

        // CheckPasswordSignInAsync touches neither the HttpContext nor the scheme provider, so
        // these are present to satisfy the constructor and nothing more.
        return new SignInManager<AppUser>(
            users,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new UserClaimsPrincipalFactory<AppUser>(users, options),
            options,
            NullLogger<SignInManager<AppUser>>.Instance,
            new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions())),
            new DefaultUserConfirmation<AppUser>());
    }
}
