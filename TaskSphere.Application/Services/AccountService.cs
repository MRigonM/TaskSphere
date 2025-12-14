using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TaskSphere.Application.DataTransferObjects.Company;
using TaskSphere.Application.DataTransferObjects.Identity;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace TaskSphere.Application.Services;

public class AccountService : IAccountService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountService> _logger;
    private readonly ICompanyService _companyService;

    public AccountService(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration,
        ILogger<AccountService> logger,
        ICompanyService companyService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _configuration = configuration;
        _logger = logger;
        _companyService = companyService;
    }
    public async Task<Result<AuthResponseDto>> LoginAsync(LoginDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
                return Result<AuthResponseDto>.Failure("Invalid email or password.");

            var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, false);
            if (!result.Succeeded)
                return Result<AuthResponseDto>.Failure("Invalid email or password.");

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? Roles.User;

            var token = GenerateJwtToken(user, role);

            return Result<AuthResponseDto>.Success(new AuthResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email ?? string.Empty,
                Role = role,
                CompanyId = user.CompanyId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while logging in: {Email}", dto.Email);
            return Result<AuthResponseDto>.Failure("Unexpected error during login.");
        }
    }
    
    public async Task<Result<string>> RegisterAsync(RegisterDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Registering new Company with email: {Email}", dto.Email);

            var user = new AppUser
            {
                UserName = dto.Email,
                Email = dto.Email,
                Name = dto.Name
            };
            
            if (dto.Password != dto.ConfirmPassword)
                return Result<string>.Failure("Passwords do not match.");
            
            var createUser = await _userManager.CreateAsync(user, dto.Password);
            if (!createUser.Succeeded)
            {
                var errors = string.Join(", ", createUser.Errors.Select(e => e.Description));
                return Result<string>.Failure($"Registration failed: {errors}");
            }

            if (!await _roleManager.RoleExistsAsync(Roles.Company))
                await _roleManager.CreateAsync(new IdentityRole(Roles.Company));

            var companyResult = await _companyService.CreateAsync(new CompanyDto { Name = dto.Name }, cancellationToken);
            if (!companyResult.IsSuccess || companyResult.Value == null)
            {
                _logger.LogWarning("Identity created but Company entity failed for {Email}", dto.Email);
                await _userManager.DeleteAsync(user);
                
                return Result<string>.Failure("Company entity creation failed.");
            }

            user.CompanyId = companyResult.Value.Id;
            var updateUser = await _userManager.UpdateAsync(user);
            if (!updateUser.Succeeded)
            {
                var errors = string.Join(", ", updateUser.Errors.Select(e => e.Description));
                await _userManager.DeleteAsync(user);
                return Result<string>.Failure($"Failed to link user to company: {errors}");
            }

            await _userManager.AddToRoleAsync(user, Roles.Company);

            _logger.LogInformation("Successfully registered Company with UserId: {UserId}, CompanyId: {CompanyId}", user.Id, user.CompanyId);
            return Result<string>.Success("Company registered successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while registering Company: {Email}", dto.Email);
            return Result<string>.Failure("Unexpected error during company registration.");
        }
    }
    
    public async Task<Result<string>> CreateUserForCompanyAsync(RegisterDto dto, Guid companyId, CancellationToken ct = default)
    {
        try
        {
            var user = new AppUser
            {
                UserName = dto.Email,
                Email = dto.Email,
                Name = dto.Name,
                CompanyId = companyId
            };
            
            if (dto.Password != dto.ConfirmPassword)
                return Result<string>.Failure("Passwords do not match.");
    
            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return Result<string>.Failure($"User creation failed: {errors}");
            }

            if (!await _roleManager.RoleExistsAsync(Roles.User))
                await _roleManager.CreateAsync(new IdentityRole(Roles.User));

            await _userManager.AddToRoleAsync(user, Roles.User);

            return Result<string>.Success("User created successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating user for company {CompanyId}", companyId);
            return Result<string>.Failure("Unexpected error during user creation.");
        }
    }
    
    private string GenerateJwtToken(AppUser user, string role)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
        };

        if (user.CompanyId.HasValue)
            claims.Add(new Claim("companyId", user.CompanyId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(10),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}