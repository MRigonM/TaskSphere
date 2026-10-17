using System.Reflection;
using Microsoft.AspNetCore.RateLimiting;
using TaskSphere.Controllers;
using TaskSphere.RateLimiting;

namespace TaskSphere.Tests.Domain;

/// <summary>
/// The probe endpoints in RateLimitingTests prove the policies; this proves the real actions carry
/// them. A missing attribute would leave an action on the global limits only, silently.
/// </summary>
public class AccountRateLimitAttributeTests
{
    [Theory]
    [InlineData(nameof(AccountController.Login), RateLimitPolicies.Login)]
    [InlineData(nameof(AccountController.Register), RateLimitPolicies.Account)]
    [InlineData(nameof(AccountController.VerifyEmail), RateLimitPolicies.Account)]
    [InlineData(nameof(AccountController.ResendVerification), RateLimitPolicies.Account)]
    [InlineData(nameof(AccountController.AcceptInvite), RateLimitPolicies.Account)]
    [InlineData(nameof(AccountController.ForgotPassword), RateLimitPolicies.Account)]
    [InlineData(nameof(AccountController.ResetPassword), RateLimitPolicies.Account)]
    [InlineData(nameof(AccountController.CreateUser), RateLimitPolicies.Invite)]
    public void The_action_carries_its_policy(string action, string policy)
    {
        var attribute = typeof(AccountController).GetMethod(action)!.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(policy, attribute!.PolicyName);
    }
}
