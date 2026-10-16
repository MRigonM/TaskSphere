using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Agent;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class AgentService : IAgentService
{
    private const int MaxNameLength = 100; // AppUser.Name's column length

    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public AgentService(
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IUnitOfWork unitOfWork,
        TimeProvider time)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<Result<AgentDto>> CreateForOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        var owner = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == ownerUserId, ct);
        if (owner is null || owner.IsAgent || owner.CompanyId is null)
            return Result<AgentDto>.Failure(Error.NotFound("User"));

        // Soft-deleted agents included: the unique index on OwnerUserId forbids a second row.
        var existing = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.OwnerUserId == ownerUserId, ct);

        if (existing is { IsDeleted: false })
            return Result<AgentDto>.Failure(EntityError.Conflict("You already have a Claude."));

        if (existing is not null)
        {
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            var revived = await _userManager.UpdateAsync(existing);
            return revived.Succeeded
                ? Result<AgentDto>.Success(new AgentDto(existing.Id, existing.Name))
                : Result<AgentDto>.Failure(Describe(revived));
        }

        var agent = new AppUser
        {
            // Identity requires a unique user name; an agent has no email to use for it.
            UserName = $"agent-{Guid.NewGuid():N}",
            Name = AgentName(owner.Name),
            CompanyId = owner.CompanyId,
            IsAgent = true,
            OwnerUserId = owner.Id,
        };

        var created = await _userManager.CreateAsync(agent); // no password: it can never log in
        if (!created.Succeeded)
            return Result<AgentDto>.Failure(Describe(created));

        if (!await _roleManager.RoleExistsAsync(Roles.User))
            await _roleManager.CreateAsync(new IdentityRole(Roles.User));

        var role = await _userManager.AddToRoleAsync(agent, Roles.User);
        if (!role.Succeeded)
        {
            // Without a role the agent is useless, and leaving it would block the owner's next
            // attempt through the unique index.
            await _userManager.DeleteAsync(agent);
            return Result<AgentDto>.Failure(Describe(role));
        }

        return Result<AgentDto>.Success(new AgentDto(agent.Id, agent.Name));
    }

    public Task<Result<AgentStatusDto>> GetMineAsync(string ownerUserId, CancellationToken ct = default)
        => throw new NotImplementedException("Task 9");

    public Task<Result<IssuedAgentTokenDto>> RegenerateTokenAsync(string ownerUserId, CancellationToken ct = default)
        => throw new NotImplementedException("Task 9");

    public Task<Result<string>> RevokeTokenAsync(string ownerUserId, CancellationToken ct = default)
        => throw new NotImplementedException("Task 9");

    public Task<AgentIdentity?> AuthenticateAsync(string token, CancellationToken ct = default)
        => throw new NotImplementedException("Task 9");

    private static string AgentName(string ownerName)
    {
        var name = $"{ownerName}'s Claude";
        return name.Length <= MaxNameLength ? name : name[..MaxNameLength];
    }

    private static Error Describe(IdentityResult result) =>
        new("General.Error", string.Join(", ", result.Errors.Select(e => e.Description)));
}
