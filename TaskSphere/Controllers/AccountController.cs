using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.DataTransferObjects.Identity;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Enums;

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
    
    [Authorize(Roles = Roles.Company)]
    [HttpPost("CreateUser")]
    public async Task<IActionResult> CreateUser([FromBody] RegisterDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetCompanyId(out var companyId))
            return Unauthorized("Missing companyId claim.");

        var result = await _accountService.CreateUserForCompanyAsync(dto, companyId, cancellationToken);
        return FromResult(result);
    }
}