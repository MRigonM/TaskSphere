using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    
    public async Task<Result<IEnumerable<UserDto>>> GetUsersAsync(Guid companyId, UserQueryDto query, CancellationToken ct = default)
    {
        try
        {
            var q = _userManager.Users
                .AsNoTracking()
                .Where(u => u.CompanyId == companyId && !u.IsDeleted);

            if (!string.IsNullOrWhiteSpace(query.Name))
            {
                var name = query.Name.Trim();
                q = q.Where(u => u.Name.Contains(name));
            }

            if (!string.IsNullOrWhiteSpace(query.Email))
            {
                var email = query.Email.Trim();
                q = q.Where(u => (u.Email ?? "").Contains(email));
            }

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

            var users = await q
                .OrderBy(u => u.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Name = u.Name,
                    Email = u.Email ?? "",
                    CompanyId = u.CompanyId,
                    IsDeleted = u.IsDeleted
                })
                .ToListAsync(ct);

            return Result<IEnumerable<UserDto>>.Success(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while retrieving users for company {CompanyId}", companyId);
            return Result<IEnumerable<UserDto>>.Failure("Unexpected error while retrieving users.");
        }
    }

    public async Task<Result<UserDto>> UpdateUserAsync(Guid companyId, string userId, UpdateUserDto dto, CancellationToken ct = default)
    {
        try
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId && !u.IsDeleted, ct);

            if (user == null)
                return Result<UserDto>.Failure("User not found.");

            user.Name = dto.Name.Trim();
            user.Email = dto.Email.Trim();

            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded)
            {
                var errors = string.Join(", ", update.Errors.Select(e => e.Description));
                return Result<UserDto>.Failure($"Update failed: {errors}");
            }

            if (!string.IsNullOrWhiteSpace(dto.NewPassword) || !string.IsNullOrWhiteSpace(dto.ConfirmNewPassword))
            {
                if (dto.NewPassword != dto.ConfirmNewPassword)
                    return Result<UserDto>.Failure("Passwords do not match.");

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var reset = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword!);

                if (!reset.Succeeded)
                {
                    var errors = string.Join(", ", reset.Errors.Select(e => e.Description));
                    return Result<UserDto>.Failure($"Password update failed: {errors}");
                }
            }

            return Result<UserDto>.Success(new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email ?? "",
                CompanyId = user.CompanyId,
                IsDeleted = user.IsDeleted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating user {UserId} for company {CompanyId}", userId, companyId);
            return Result<UserDto>.Failure("Unexpected error while updating user.");
        }
    }

    public async Task<Result<string>> DeleteUserAsync(Guid companyId, string userId, CancellationToken ct = default)
    {
        try
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId && !u.IsDeleted, ct);

            if (user == null)
                return Result<string>.Failure("User not found.");

            user.IsDeleted = true;
            user.DeletedAt = DateTimeOffset.UtcNow;

            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded)
            {
                var errors = string.Join(", ", update.Errors.Select(e => e.Description));
                return Result<string>.Failure($"Delete failed: {errors}");
            }

            return Result<string>.Success("User deleted successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while deleting user {UserId} for company {CompanyId}", userId, companyId);
            return Result<string>.Failure("Unexpected error while deleting user.");
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