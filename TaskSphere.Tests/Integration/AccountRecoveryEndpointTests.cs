using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Controllers;
using TaskSphere.Domain.DataTransferObjects.Identity;

namespace TaskSphere.Tests.Integration;

public class AccountRecoveryEndpointTests
{
    private static MethodInfo Action(string name) =>
        typeof(AccountController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"{name} is missing from AccountController.");

    [Theory]
    [InlineData(nameof(AccountController.AcceptInvite), "AcceptInvite")]
    [InlineData(nameof(AccountController.ForgotPassword), "ForgotPassword")]
    [InlineData(nameof(AccountController.ResetPassword), "ResetPassword")]
    public void Posts_to_its_own_route(string method, string expectedRoute)
    {
        var post = Action(method).GetCustomAttribute<HttpPostAttribute>();

        Assert.NotNull(post);
        Assert.Equal(expectedRoute, post!.Template);
    }

    [Theory]
    [InlineData(nameof(AccountController.AcceptInvite))]
    [InlineData(nameof(AccountController.ForgotPassword))]
    [InlineData(nameof(AccountController.ResetPassword))]
    public void Is_reachable_without_a_token(string method)
    {
        // All three are used by people who cannot log in — an invited member who has no password
        // yet, and anyone who has forgotten theirs.
        Assert.NotNull(Action(method).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Create_user_now_takes_an_invitation_rather_than_a_registration()
    {
        var parameter = Action(nameof(AccountController.CreateUser)).GetParameters()[0];

        // RegisterDto would carry password fields an admin must not be able to set for someone
        // else, and would drag RegisterValidator's password rule onto a body that has none.
        Assert.Equal(typeof(InviteUserDto), parameter.ParameterType);
    }
}
