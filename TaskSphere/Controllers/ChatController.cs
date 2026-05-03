using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize]
[RequireCompany]
public class ChatController : ApiBaseController
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpGet("projects/{projectId:int}/messages")]
    public async Task<IActionResult> GetMessages(int projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _chatService.GetMessagesAsync(CompanyId, UserId, projectId, page, pageSize, ct);
        return FromResult(result);
    }
}