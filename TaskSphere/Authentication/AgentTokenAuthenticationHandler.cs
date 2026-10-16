using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace TaskSphere.Authentication;

/// <summary>
/// Turns <c>Authorization: Bearer tsk_…</c> into the same claims AccountService puts in a JWT,
/// so [RequireCompany], ApiBaseController.UserId and IAccessControlService work unchanged. Any
/// other header is NoResult, never Fail: this handler does not own non-agent credentials.
/// </summary>
public sealed class AgentTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AgentToken";
    public const string AgentClaim = "isAgent";
    private const string BearerPrefix = "Bearer ";

    private readonly IAgentService _agents;

    public AgentTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IAgentService agents)
        : base(options, logger, encoder)
    {
        _agents = agents;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header[BearerPrefix.Length..].Trim();
        if (!AgentTokenFormat.LooksLikeAgentToken(token))
            return AuthenticateResult.NoResult();

        var agent = await _agents.AuthenticateAsync(token, Context.RequestAborted);
        if (agent is null)
            return AuthenticateResult.Fail("Invalid or revoked agent token.");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, agent.AgentUserId),
            new Claim(ClaimTypes.NameIdentifier, agent.AgentUserId),
            new Claim(ClaimTypes.Name, agent.Name),
            new Claim(ClaimTypes.Role, Roles.User),
            new Claim("companyId", agent.CompanyId.ToString()),
            new Claim(AgentClaim, "true"),
        };

        // Name/role claim types match the JWT setup in AuthenticationExtensions.
        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.NameIdentifier, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
