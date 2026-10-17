using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskSphere.Application.Interfaces;
using TaskSphere.Application.Settings;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Company;
using TaskSphere.Domain.DataTransferObjects.Identity;
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
    private readonly IAccountVerificationService _verification;
    private readonly IEmailSender _emailSender;
    private readonly ClientOptions _client;
    private readonly IAgentService _agents;

    public AccountService(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration,
        ILogger<AccountService> logger,
        ICompanyService companyService,
        IAccountVerificationService verification,
        IEmailSender emailSender,
        IOptions<ClientOptions> client,
        IAgentService agents)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _configuration = configuration;
        _logger = logger;
        _companyService = companyService;
        _verification = verification;
        _emailSender = emailSender;
        _client = client.Value;
        _agents = agents;
    }
    public async Task<Result<AuthResponseDto>> LoginAsync(LoginDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
                return Result<AuthResponseDto>.Failure("Invalid email or password.");

            var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);

            // Before the generic answer: a locked account must say so, or the user keeps trying
            // a right password and never learns why it fails.
            if (result.IsLockedOut)
                return Result<AuthResponseDto>.Failure(new Error(
                    "Auth.LockedOut",
                    "Too many failed attempts. Try again in a few minutes."));

            if (!result.Succeeded)
                return Result<AuthResponseDto>.Failure("Invalid email or password.");

            // After the password check, never before: an unverified-account answer returned for a
            // password nobody guessed would tell an outsider which addresses are registered.
            if (!user.EmailConfirmed)
                return Result<AuthResponseDto>.Failure(new Error(
                    "Auth.EmailNotConfirmed",
                    "Confirm your email address before logging in."));

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? Roles.User;

            var token = GenerateJwtToken(user, role);

            var isPersonal = false;
            if (user.CompanyId.HasValue)
            {
                var company = await _companyService.GetByIdAsync(user.CompanyId.Value, cancellationToken);
                isPersonal = company.IsSuccess && company.Value!.IsPersonal;
            }

            return Result<AuthResponseDto>.Success(new AuthResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email ?? string.Empty,
                Name = user.Name,
                Role = role,
                CompanyId = user.CompanyId,
                IsPersonal = isPersonal
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

            var companyResult = await _companyService.CreateAsync(
                new CompanyDto { Name = dto.Name, IsPersonal = dto.IsPersonal }, cancellationToken);
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

            if (dto.IsPersonal)
            {
                var agent = await _agents.CreateForOwnerAsync(user.Id, cancellationToken);
                if (!agent.IsSuccess)
                {
                    // Same compensation as the steps above. The company row is left behind, as
                    // it is when the link step fails; registration is not transactional.
                    _logger.LogWarning("Personal registration could not create the agent for {Email}", dto.Email);
                    await _userManager.DeleteAsync(user);
                    return Result<string>.Failure("Could not set up your Claude. Please try again.");
                }
            }

            _logger.LogInformation("Successfully registered Company with UserId: {UserId}, CompanyId: {CompanyId}", user.Id, user.CompanyId);
            // The account is kept whatever happens to the mail: a transient SMTP failure must not
            // destroy a company that was created correctly.
            var sent = await _verification.SendVerificationAsync(user.Email!, cancellationToken);

            return Result<string>.Success(sent.IsSuccess
                ? "Account created. Check your email to verify your address."
                : "Account created, but the verification email could not be sent. Use Resend on the login page.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while registering Company: {Email}", dto.Email);
            return Result<string>.Failure("Unexpected error during company registration.");
        }
    }
    
    public async Task<Result<string>> CreateUserForCompanyAsync(InviteUserDto dto, Guid companyId, CancellationToken ct = default)
    {
        try
        {
            var workspace = await _companyService.GetByIdAsync(companyId, ct);
            if (workspace.IsSuccess && workspace.Value!.IsPersonal)
                return Result<string>.Failure(new Error(
                    "Auth.Forbidden", "A personal workspace has only you in it, so members cannot be invited."));

            var user = new AppUser
            {
                UserName = dto.Email,
                Email = dto.Email,
                Name = dto.Name,
                CompanyId = companyId
            };

            // No password: the member chooses their own through the invitation link. An admin who
            // picks a password for someone else has to transmit it somehow, and every way of
            // doing that is worse than a link.
            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return Result<string>.Failure($"User creation failed: {errors}");
            }

            if (!await _roleManager.RoleExistsAsync(Roles.User))
                await _roleManager.CreateAsync(new IdentityRole(Roles.User));

            await _userManager.AddToRoleAsync(user, Roles.User);

            var company = workspace;
            var companyName = company.IsSuccess ? company.Value!.Name : "Your team";

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = AccountEmails.AcceptInviteLink(_client.BaseUrl, dto.Email, token);
            var (subject, body) = AccountEmails.Invitation(companyName, link);

            var sent = await _emailSender.SendAsync(dto.Email, subject, body, ct);

            return Result<string>.Success(sent.IsSuccess
                ? "Member added. They have been emailed a link to set their password."
                : "Member added, but the invitation email could not be sent.");
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
                    IsDeleted = u.IsDeleted,
                    IsAgent = u.IsAgent
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

            if (user.IsAgent)
                return Result<UserDto>.Failure("A Claude is managed from its owner's My Claude page, not edited here.");

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
            new Claim(ClaimTypes.Name, user.Name),
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
