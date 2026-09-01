using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Controllers;

namespace TaskSphere.Tests.Integration;

public class AccountVerificationEndpointTests
{
    private static MethodInfo Action(string name) =>
        typeof(AccountController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"{name} is missing from AccountController.");

    [Theory]
    [InlineData(nameof(AccountController.VerifyEmail), "VerifyEmail")]
    [InlineData(nameof(AccountController.ResendVerification), "ResendVerification")]
    public void Posts_to_its_own_route(string method, string expectedRoute)
    {
        var post = Action(method).GetCustomAttribute<HttpPostAttribute>();

        Assert.NotNull(post);
        Assert.Equal(expectedRoute, post!.Template);
    }

    [Theory]
    [InlineData(nameof(AccountController.VerifyEmail))]
    [InlineData(nameof(AccountController.ResendVerification))]
    public void Is_reachable_without_a_token(string method)
    {
        // Both are used by people who cannot log in yet. An [Authorize] here would make the
        // feature unreachable by exactly the users it exists for.
        Assert.NotNull(Action(method).GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
