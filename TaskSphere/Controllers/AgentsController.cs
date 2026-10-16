using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

/// <summary>
/// The caller's own Claude. Every action acts on the caller's agent and no one else's — there is
/// no agent id in any route — and the default JWT scheme applies, so agent tokens are refused.
/// </summary>
[Authorize(Roles = Roles.CompanyOrUser)]
[RequireCompany]
[Route("api/[controller]")]
public class AgentsController : ApiBaseController
{
    private readonly IAgentService _agents;

    public AgentsController(IAgentService agents)
    {
        _agents = agents;
    }

    [Audit("Created a Claude agent")]
    [HttpPost("me")]
    public async Task<IActionResult> Create(CancellationToken ct)
        => FromResult(await _agents.CreateForOwnerAsync(UserId, ct));

    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => FromResult(await _agents.GetMineAsync(UserId, ct));

    [Audit("Regenerated a Claude token")]
    [HttpPost("me/token")]
    public async Task<IActionResult> RegenerateToken(CancellationToken ct)
        => FromResult(await _agents.RegenerateTokenAsync(UserId, ct));

    [Audit("Revoked a Claude token")]
    [HttpDelete("me/token")]
    public async Task<IActionResult> RevokeToken(CancellationToken ct)
        => FromResult(await _agents.RevokeTokenAsync(UserId, ct));
}
