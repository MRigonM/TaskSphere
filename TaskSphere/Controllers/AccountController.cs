using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Route("api/[controller]")]
public class AccountController : ApiBaseController
{
    private readonly IAccountService _accountService;
    private readonly IAccountVerificationService _verificationService;

    public AccountController(
        IAccountService accountService,
        IAccountVerificationService verificationService)
    {
        _accountService = accountService;
        _verificationService = verificationService;
    }

    [AllowAnonymous]
    [HttpPost("Register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto, CancellationToken cancellationToken)
    {
        var result = await _accountService.RegisterAsync(dto, cancellationToken);
        return FromResult(result);
    }

    [AllowAnonymous]
    [HttpPost("Login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto, CancellationToken cancellationToken)
    {
        var result = await _accountService.LoginAsync(dto, cancellationToken);
        return FromResult(result);
    }

    [AllowAnonymous]
    [HttpPost("VerifyEmail")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto, CancellationToken ct)
    {
        var result = await _verificationService.VerifyEmailAsync(dto, ct);
        return FromResult(result);
    }

    [AllowAnonymous]
    [HttpPost("ResendVerification")]
    public async Task<IActionResult> ResendVerification([FromBody] EmailOnlyDto dto, CancellationToken ct)
    {
        var result = await _verificationService.ResendVerificationAsync(dto, ct);
        return FromResult(result);
    }

    [AllowAnonymous]
    [HttpPost("AcceptInvite")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteDto dto, CancellationToken ct)
    {
        var result = await _verificationService.AcceptInviteAsync(dto, ct);
        return FromResult(result);
    }

    [AllowAnonymous]
    [HttpPost("ForgotPassword")]
    public async Task<IActionResult> ForgotPassword([FromBody] EmailOnlyDto dto, CancellationToken ct)
    {
        var result = await _verificationService.ForgotPasswordAsync(dto, ct);
        return FromResult(result);
    }

    [AllowAnonymous]
    [HttpPost("ResetPassword")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto, CancellationToken ct)
    {
        var result = await _verificationService.ResetPasswordAsync(dto, ct);
        return FromResult(result);
    }

    [Audit("Created a user")]
    [Authorize(Roles = Roles.Company)]
    [RequireCompany]
    [HttpPost("CreateUser")]
    public async Task<IActionResult> CreateUser([FromBody] InviteUserDto dto, CancellationToken cancellationToken)
    {
        var result = await _accountService.CreateUserForCompanyAsync(dto, CompanyId, cancellationToken);
        return FromResult(result);
    }

    [Authorize(Roles = Roles.CompanyOrUser)]
    [RequireCompany]
    [HttpGet("Users")]
    public async Task<IActionResult> GetUsers([FromQuery] UserQueryDto query, CancellationToken ct)
    {
        var result = await _accountService.GetUsersAsync(CompanyId, query, ct);
        return FromResult(result);
    }

    [Audit("Updated a user")]
    [Authorize(Roles = Roles.Company)]
    [RequireCompany]
    [HttpPut("Users/{userId}")]
    public async Task<IActionResult> UpdateUser(string userId, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        var result = await _accountService.UpdateUserAsync(CompanyId, userId, dto, ct);
        return FromResult(result);
    }

    [Audit("Deleted a user")]
    [Authorize(Roles = Roles.Company)]
    [RequireCompany]
    [HttpDelete("Users/{userId}")]
    public async Task<IActionResult> DeleteUser(string userId, CancellationToken ct)
    {
        var result = await _accountService.DeleteUserAsync(CompanyId, userId, ct);
        return FromResult(result);
    }
}