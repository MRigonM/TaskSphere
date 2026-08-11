using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Auditing;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize(Roles = Roles.Company)]
[RequireCompany]
[Route("api/[controller]")]
public class GitHubController : ApiBaseController
{
    private readonly IGitHubConnectionService _connectionService;
    private readonly IGitHubConnectionReadService _readService;
    private readonly IGitHubProjectLinkService _linkService;

    public GitHubController(
        IGitHubConnectionService connectionService,
        IGitHubConnectionReadService readService,
        IGitHubProjectLinkService linkService)
    {
        _connectionService = connectionService;
        _readService = readService;
        _linkService = linkService;
    }

    /// <summary>
    /// Returns the GitHub App installation URL for the SPA to navigate to. Not a 302: the
    /// caller is an XHR and cannot follow a redirect to github.com (§0g).
    /// </summary>
    [HttpGet("install")]
    public IActionResult GetInstallUrl()
    {//1,10328
        var result = _connectionService.GetInstallUrl(CompanyId, UserId);
        return FromResult(result);
    }

    /// <summary>
    /// Completes the install. GitHub redirects the browser to the Angular route, which POSTs
    /// here with a token attached — the redirect itself carries no Authorization header (§0g).
    /// </summary>
    // Deliberately NOT audited. AuditAttribute serialises every action argument, and
    // SensitiveDataRedactor only redacts property names containing password/token/secret/pwd —
    // so ConnectGitHubDto.Code and .State would land in AuditLogs verbatim. Extending the
    // redactor was rejected: IsSensitive matches with Contains, so adding "code" would redact
    // every action argument whose property name ends in Code. No other request DTO has one
    // today, but an invite/coupon/postal Code would silently become "***" — too broad a change
    // to the whole API for one endpoint's problem.
    [HttpPost("callback")]
    public async Task<IActionResult> Callback([FromBody] ConnectGitHubDto dto, CancellationToken ct)
    {
        var result = await _connectionService.ConnectAsync(CompanyId, UserId, dto, ct);
        return FromResult(result);
    }

    [HttpGet("connection")]
    public async Task<IActionResult> GetConnection(CancellationToken ct)
    {
        var result = await _readService.GetConnectionAsync(CompanyId, ct);
        return FromResult(result);
    }

    [Audit("Disconnected the GitHub organization")]
    [HttpDelete("connection")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var result = await _readService.DisconnectAsync(CompanyId, ct);
        return FromResult(result);
    }

    [Audit("Linked a repository to a project")]
    [HttpPost("projects/{projectId:int}/repositories")]
    public async Task<IActionResult> Link(int projectId, [FromBody] LinkRepositoryDto dto, CancellationToken ct)
    {
        var result = await _linkService.LinkAsync(CompanyId, UserId, projectId, dto.RepositoryId, ct);
        return FromResult(result);
    }

    [Audit("Unlinked a repository from a project")]
    [HttpDelete("projects/{projectId:int}/repositories/{repositoryId:int}")]
    public async Task<IActionResult> Unlink(int projectId, int repositoryId, CancellationToken ct)
    {
        var result = await _linkService.UnlinkAsync(CompanyId, projectId, repositoryId, ct);
        return FromResult(result);
    }

    /// <summary>
    /// The one endpoint a project <c>Member</c> can reach, so the role gate is widened here and
    /// membership is enforced in the service instead (§0h).
    /// </summary>
    [Authorize(Roles = Roles.CompanyOrUser)]
    [HttpGet("projects/{projectId:int}/repositories")]
    public async Task<IActionResult> GetProjectRepositories(int projectId, CancellationToken ct)
    {
        var result = await _linkService.GetProjectRepositoriesAsync(CompanyId, UserId, IsCompanyAdmin, projectId, ct);
        return FromResult(result);
    }

    /// <summary>
    /// Company-wide repository↔project links. Inherits the controller's Company-role gate: this
    /// returns every project's links, so it is not widened the way GetProjectRepositories is.
    /// Not audited — reads are not audited on this controller.
    /// </summary>
    [HttpGet("links")]
    public async Task<IActionResult> GetCompanyLinks(CancellationToken ct)
    {
        var result = await _linkService.GetCompanyLinksAsync(CompanyId, ct);
        return FromResult(result);
    }
}
