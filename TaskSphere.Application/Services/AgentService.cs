using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Agent;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Entities.Identity;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class AgentService : IAgentService
{
    private const int MaxNameLength = 100; // AppUser.Name's column length
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromSeconds(60);

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

    public async Task<Result<AgentStatusDto>> GetMineAsync(string ownerUserId, CancellationToken ct = default)
    {
        var agent = await FindAgentAsync(ownerUserId, ct);
        if (agent is null)
            return Result<AgentStatusDto>.Success(new AgentStatusDto(false, null, null));

        var live = await _unitOfWork.AgentTokens.GetLiveForAgentAsync(agent.Id, ct);
        return Result<AgentStatusDto>.Success(new AgentStatusDto(
            true,
            agent.Name,
            live is null ? null : new AgentTokenInfoDto(live.Prefix, live.CreatedAtUtc, live.LastUsedAtUtc)));
    }

    public async Task<Result<IssuedAgentTokenDto>> RegenerateTokenAsync(string ownerUserId, CancellationToken ct = default)
    {
        var agent = await FindAgentAsync(ownerUserId, ct);
        if (agent is null)
            return Result<IssuedAgentTokenDto>.Failure(Error.NotFound("Your Claude"));

        // Two saves, not one: EF orders a batch by key and index *columns*, and RevokedAtUtc is
        // only in the index filter, so revoke-then-insert in a single batch is not guaranteed to
        // reach SQL Server in that order. If the insert then fails, the owner has no token and
        // presses Regenerate again — a recoverable state, unlike two live tokens.
        var live = await _unitOfWork.AgentTokens.GetLiveForAgentAsync(agent.Id, ct);
        if (live is not null)
        {
            live.RevokedAtUtc = _time.GetUtcNow().UtcDateTime;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var (token, hash, prefix) = AgentTokenFormat.Create();
        var row = new AgentToken { AgentUserId = agent.Id, TokenHash = hash, Prefix = prefix };
        await _unitOfWork.AgentTokens.AddAsync(row, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        // A concurrent regenerate won the race to the one live slot. Matched by index name, as
        // ProjectService does, because this layer must not reference SqlClient.
        catch (DbUpdateException ex) when (
            ex.GetBaseException().Message.Contains("IX_AgentTokens_AgentUserId_Live", StringComparison.Ordinal))
        {
            _unitOfWork.DiscardPendingChanges();
            return Result<IssuedAgentTokenDto>.Failure(
                EntityError.Conflict("Another token was issued at the same moment. Try again."));
        }

        return Result<IssuedAgentTokenDto>.Success(new IssuedAgentTokenDto(token, prefix, row.CreatedAtUtc));
    }

    public async Task<Result<string>> RevokeTokenAsync(string ownerUserId, CancellationToken ct = default)
    {
        var agent = await FindAgentAsync(ownerUserId, ct);
        if (agent is null)
            return Result<string>.Failure(Error.NotFound("Your Claude"));

        var live = await _unitOfWork.AgentTokens.GetLiveForAgentAsync(agent.Id, ct);
        if (live is null)
            return Result<string>.Failure(Error.NotFound("A live token"));

        live.RevokedAtUtc = _time.GetUtcNow().UtcDateTime;
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<string>.Success("Token revoked.");
    }

    public async Task<AgentIdentity?> AuthenticateAsync(string token, CancellationToken ct = default)
    {
        if (!AgentTokenFormat.LooksLikeAgentToken(token))
            return null;

        // The AgentToken query filter already hides tokens of a soft-deleted agent.
        var row = await _unitOfWork.AgentTokens.FindLiveByHashAsync(AgentTokenFormat.Hash(token), ct);
        if (row is null)
            return null;

        var agent = row.Agent;
        if (!agent.IsAgent || agent.CompanyId is null || agent.OwnerUserId is null)
            return null;

        // The token dies with its owner: a soft-deleted owner is filtered out here.
        var owner = await _userManager.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == agent.OwnerUserId, ct);
        if (owner is null || owner.CompanyId != agent.CompanyId)
            return null;

        var now = _time.GetUtcNow().UtcDateTime;
        if (row.LastUsedAtUtc is null || now - row.LastUsedAtUtc.Value >= LastUsedResolution)
        {
            row.LastUsedAtUtc = now;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return new AgentIdentity(agent.Id, agent.Name, agent.CompanyId.Value);
    }

    private Task<AppUser?> FindAgentAsync(string ownerUserId, CancellationToken ct) =>
        _userManager.Users.FirstOrDefaultAsync(u => u.OwnerUserId == ownerUserId && u.IsAgent, ct);

    private static string AgentName(string ownerName)
    {
        var name = $"{ownerName}'s Claude";
        return name.Length <= MaxNameLength ? name : name[..MaxNameLength];
    }

    private static Error Describe(IdentityResult result) =>
        new("General.Error", string.Join(", ", result.Errors.Select(e => e.Description)));
}
