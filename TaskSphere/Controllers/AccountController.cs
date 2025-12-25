using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.DataTransferObjects.Identity;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

public class AccountController : ApiBaseController
{
    private readonly IAccountService _accountService;

    public AccountController(IAccountService accountService)
    {
        _accountService = accountService;
    }
    
    [AllowAnonymous]
    [HttpPost("Register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto,
        CancellationToken cancellationToken)
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
    
    [RequireCompany]
    [Authorize(Roles = Roles.Company)]
    [HttpPost("CreateUser")]
    public async Task<IActionResult> CreateUser([FromBody] RegisterDto dto, CancellationToken cancellationToken)
    {
        var result = await _accountService.CreateUserForCompanyAsync(dto, CompanyId, cancellationToken);
        return FromResult(result);
    }
    
    [Authorize(Roles = Roles.Company)]
    [HttpGet("Users")]
    public async Task<IActionResult> GetUsers([FromQuery] UserQueryDto query, CancellationToken ct)
    {
       var result = await _accountService.GetUsersAsync(CompanyId, query, ct);
        return FromResult(result);
    }

    [Authorize(Roles = Roles.Company)]
    [HttpPut("Users/{userId}")]
    public async Task<IActionResult> UpdateUser(string userId, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        var result = await _accountService.UpdateUserAsync(CompanyId, userId, dto, ct);
        return FromResult(result);
    }

    [Authorize(Roles = Roles.Company)]
    [HttpDelete("Users/{userId}")]
    public async Task<IActionResult> DeleteUser(string userId, CancellationToken ct)
    {
        var result = await _accountService.DeleteUserAsync(CompanyId, userId, ct);
        return FromResult(result);
    }
}